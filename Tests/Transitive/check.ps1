# Restores REAL consumers of a built SpawnDev.SpawnJS.WebWorkers nupkg and fails unless an app that gets the package only
# INDIRECTLY still gets the worker bundle, and a non-WASM project that gets it indirectly does nothing.
#   powershell -ExecutionPolicy Bypass -File Tests\Transitive\check.ps1                      (newest bin\Release nupkg)
#   powershell -ExecutionPolicy Bypass -File Tests\Transitive\check.ps1 -Nupkg <path> [-Keep]
# Regression gate for <= 2.2.3: NuGet writes exclude="Build,Analyzers" on a package's dependencies, so an app that
# references only e.g. SpawnDev.SpawnJS.BrowserExtension imported buildTransitive\*.props and never build\*.targets:
# no main.classic.js / main.module.js, no bundle attribute, silently.
# Uses a throwaway folder + isolated NUGET_PACKAGES, so nothing lands in the global package cache or the local feed.
param([string]$Nupkg = '', [switch]$Keep)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Nupkg) {
    $Nupkg = Get-ChildItem (Join-Path $root 'SpawnDev.SpawnJS.WebWorkers\bin\Release\SpawnDev.SpawnJS.WebWorkers.*.nupkg') |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
$ver = [regex]::Match([IO.Path]::GetFileName($Nupkg), '^SpawnDev\.SpawnJS\.WebWorkers\.(.+)\.nupkg$').Groups[1].Value
if (-not $ver) { throw "not a SpawnDev.SpawnJS.WebWorkers nupkg: $Nupkg" }
Write-Host "package under test: $Nupkg ($ver)"

$work = Join-Path ([IO.Path]::GetTempPath()) ('spawnjs-ww-transitive-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force "$work\feed" | Out-Null
Copy-Item $Nupkg "$work\feed\"
$env:NUGET_PACKAGES = "$work\packages"
Set-Content "$work\nuget.config" @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="test" value="feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

function New-Proj($name, $sdk, $refs, $extra = '') {
    New-Item -ItemType Directory -Force "$work\$name" | Out-Null
    Set-Content "$work\$name\$name.csproj" @"
<Project Sdk="$sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    $extra
  </PropertyGroup>
  <ItemGroup>
    $refs
  </ItemGroup>
</Project>
"@
}
$wasm = '<OutputType>Exe</OutputType><CompressionEnabled>false</CompressionEnabled>'
# A library that depends on WebWorkers, the way SpawnDev.SpawnJS.BrowserExtension does.
New-Proj 'TransLib' 'Microsoft.NET.Sdk.Razor' "<PackageReference Include=`"SpawnDev.SpawnJS.WebWorkers`" Version=`"$ver`" />" '<Version>1.0.0</Version><PackageOutputPath>..\feed</PackageOutputPath>'
New-Proj 'AppViaPackage' 'Microsoft.NET.Sdk.WebAssembly' '<PackageReference Include="TransLib" Version="1.0.0" />' $wasm
New-Proj 'AppViaProject' 'Microsoft.NET.Sdk.WebAssembly' '<ProjectReference Include="..\TransLib\TransLib.csproj" />' $wasm
New-Proj 'AppDirect' 'Microsoft.NET.Sdk.WebAssembly' "<PackageReference Include=`"SpawnDev.SpawnJS.WebWorkers`" Version=`"$ver`" />" $wasm
# A non-WASM Exe that reaches WebWorkers through a ProjectReference (hosted server / test project shape): must do nothing.
New-Proj 'ServerViaProject' 'Microsoft.NET.Sdk.Web' '<ProjectReference Include="..\TransLib\TransLib.csproj" />'
foreach ($p in 'AppViaPackage', 'AppViaProject', 'AppDirect', 'ServerViaProject') { Set-Content "$work\$p\Program.cs" 'System.Console.WriteLine("ok");' }

$failed = 0
function Check($ok, $what) {
    if ($ok) { Write-Host "PASS  $what" } else { Write-Host "FAIL  $what"; $script:failed++ }
}
function Run($what) {
    # Every dotnet call: no node reuse and no shared compiler / Razor servers, so nothing keeps a lock on the throwaway
    # folder and no build server other agents use is touched.
    $out = & dotnet @args '-nodeReuse:false' '-nologo' '-p:UseSharedCompilation=false' '-p:UseRazorBuildServer=false' 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host $out; throw "$what failed (exit $LASTEXITCODE)" }
    return $out
}

try {
    Push-Location $work
    Run 'pack TransLib' 'pack' 'TransLib' '-c' 'Release' | Out-Null
    foreach ($p in 'AppViaPackage', 'AppViaProject', 'AppDirect', 'ServerViaProject') { Run "restore $p" 'restore' $p | Out-Null }

    # 1. Evaluation: which projects would bundle.
    $expect = @{ AppViaPackage = 'true'; AppViaProject = 'true'; AppDirect = 'true'; ServerViaProject = '' }
    foreach ($p in $expect.Keys) {
        # Two properties so msbuild prints JSON (a single -getProperty prints the bare value).
        $json = Run "evaluate $p" 'msbuild' $p '-p:Configuration=Release' '-getProperty:_SpawnJSWebWorkersBundleEnabled' '-getProperty:SpawnJSWebWorkersClassicBundle'
        $got = [string](($json | ConvertFrom-Json).Properties._SpawnJSWebWorkersBundleEnabled)
        Check ($got -eq $expect[$p]) "$p bundle enabled = '$got' (expected '$($expect[$p])')"
    }

    # 2. Publish the indirect and the direct consumer: both entrypoints exist, and the direct consumer (which now
    #    has both build\ and buildTransitive\ targets available) does not import the targets twice (MSB4011).
    foreach ($p in 'AppViaPackage', 'AppDirect') {
        $out = Run "publish $p" 'publish' $p '-c' 'Release' '-o' "$work\pub\$p"
        foreach ($f in 'main.classic.js', 'main.module.js') { Check (Test-Path "$work\pub\$p\wwwroot\$f") "$p publish has $f" }
        Check ($out -notmatch 'MSB4011') "$p imports the WebWorkers targets once (no MSB4011)"
    }

    # 3. The server builds untouched: no bundle step, no bundle attribute stamped into it.
    $out = Run 'build ServerViaProject' 'build' 'ServerViaProject' '-c' 'Release'
    Check ($out -notmatch 'SpawnJS\.WebWorkers bundle') 'ServerViaProject build runs no bundle step'
    $attr = Get-ChildItem "$work\ServerViaProject\obj" -Recurse -Filter 'SpawnJSWebWorkers*.g.cs' -ErrorAction SilentlyContinue
    Check (-not $attr) 'ServerViaProject has no SpawnJSWebWorkers*.g.cs attribute file'
}
finally {
    Pop-Location
    Remove-Item Env:\NUGET_PACKAGES
    if ($Keep) { Write-Host "kept: $work" } else { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
if ($failed -gt 0) { Write-Host "$failed check(s) failed"; exit 1 }
Write-Host 'all transitive cases pass'
