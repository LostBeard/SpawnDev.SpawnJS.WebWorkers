# Evaluates the WebWorkers build switches for three consumer shapes and fails on any mismatch.
#   powershell -ExecutionPolicy Bypass -File Tests\BuildProps\check.ps1
# Regression gate for 2.1.19: the derived defaults were computed in .props, BEFORE the consumer's csproj body, so a
# Blazor-SDK app that set SpawnJSWebWorkersBlazor=false still got ClassicBundle=false and shipped no main.classic.js.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$cases = @(
    @{ Name = 'Blazor SDK, Blazor runtime (DemoBlazor)'; Project = 'SpawnDev.SpawnJS.WebWorkers.DemoBlazor\SpawnDev.SpawnJS.WebWorkers.DemoBlazor.csproj';
       Expect = @{ SpawnJSWebWorkersBlazor = 'true'; SpawnJSWebWorkersClassicBundle = 'false'; WasmBundlerFriendlyBootConfig = '' } },
    @{ Name = 'plain WASM SDK (Demo)'; Project = 'SpawnDev.SpawnJS.WebWorkers.Demo\SpawnDev.SpawnJS.WebWorkers.Demo.csproj';
       Expect = @{ SpawnJSWebWorkersBlazor = 'false'; SpawnJSWebWorkersClassicBundle = 'true'; WasmBundlerFriendlyBootConfig = 'true' } },
    @{ Name = 'Blazor SDK, csproj opts out (RazorRenderer app / browser extension)'; Project = 'Tests\BuildProps\BlazorSdkOptOut\BlazorSdkOptOut.csproj';
       Expect = @{ SpawnJSWebWorkersBlazor = 'false'; SpawnJSWebWorkersClassicBundle = 'true'; WasmBundlerFriendlyBootConfig = 'true' } }
)
$failed = 0
foreach ($c in $cases) {
    $proj = Join-Path $root $c.Project
    $json = dotnet msbuild $proj -nologo -p:Configuration=Debug -getProperty:SpawnJSWebWorkersBlazor -getProperty:SpawnJSWebWorkersClassicBundle -getProperty:WasmBundlerFriendlyBootConfig
    if ($LASTEXITCODE -ne 0) { throw "msbuild evaluation failed for $($c.Project):`n$json" }
    $props = ($json | ConvertFrom-Json).Properties
    foreach ($k in $c.Expect.Keys) {
        $got = [string]$props.$k
        if ($got -ne $c.Expect[$k]) { Write-Host "FAIL  $($c.Name): $k = '$got', expected '$($c.Expect[$k])'"; $failed++ }
        else { Write-Host "PASS  $($c.Name): $k = '$got'" }
    }
}
if ($failed -gt 0) { Write-Host "$failed mismatch(es)"; exit 1 }
Write-Host 'all build-props cases pass'
