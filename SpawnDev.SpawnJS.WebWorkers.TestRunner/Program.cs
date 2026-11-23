using Microsoft.Playwright;
using System.Diagnostics;
using System.Text.RegularExpressions;

// SpawnDev.SpawnJS.WebWorkers test harness (a port of the SpawnJS harness).
//
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner                      run everything
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- Serialization     run tests whose name contains "Serialization"
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --headed          watch it in a real browser window
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --url http://...  use an already running dev server
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --debug           test THIS WORKING TREE (see below)
//
// Exit code is the number of failed tests, so it is usable as a gate.
//
// 🔴 WHICH BITS DOES A RUN ACTUALLY TEST? The Demo swaps its reference by configuration:
//   Release -> PackageReference to the PUBLISHED SpawnDev.SpawnJS.WebWorkers package
//   Debug   -> ProjectReference to ..\SpawnDev.SpawnJS.WebWorkers (this working tree)
// Release is the default because it is the shipping configuration, but it means an edit to the library
// is SILENTLY ignored by a default run - the suite builds and passes against bits you did not write.
// Use --debug to exercise local library changes. The configuration is printed on every run so a result
// can never be misread later.

var filter = "";
var headed = false;
var externalUrl = "";
var verbose = false;
var configuration = "Release";
// --app <project folder>: the app to test. Default: the non-Blazor Demo. SpawnDev.SpawnJS.WebWorkers.DemoBlazor covers what only a
// Blazor app exercises (a classic-mode worker boots the Blazor runtime through the module patcher).
var appDir = "SpawnDev.SpawnJS.WebWorkers.Demo";
// --published: publish the app and serve the PUBLISHED wwwroot (what a deployed app ships) instead of `dotnet run`. The dev
// server serves plain asset names too, which hides bugs that only fingerprinted output shows (a .NET 10 publish with
// OverrideHtmlAssetPlaceholders ships only dotnet.<hash>.js; a module worker asked for the plain dotnet.js and never booted).
var published = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--headed": headed = true; break;
        case "--verbose": verbose = true; break;
        case "--debug": configuration = "Debug"; break;
        case "--config": configuration = ++i < args.Length ? args[i] : "Release"; break;
        case "--url": externalUrl = ++i < args.Length ? args[i] : ""; break;
        case "--app": appDir = ++i < args.Length ? args[i] : appDir; break;
        case "--published": published = true; break;
        case "--filter": filter = ++i < args.Length ? args[i] : ""; break;
        case "-h":
        case "--help":
            Console.WriteLine("usage: [filter] [--filter <text>] [--headed] [--verbose] [--debug] [--config <name>] [--url <url>] [--app <project folder>] [--published]");
            Console.WriteLine("  --debug  build the Demo in Debug, which references the library by project (tests YOUR edits).");
            Console.WriteLine("           The default Release build references the PUBLISHED package instead.");
            return 0;
        default:
            if (!args[i].StartsWith("-")) filter = args[i];
            break;
    }
}

var repoRoot = FindRepoRoot();
var demoProject = Path.Combine(repoRoot, appDir, appDir + ".csproj");
if (!File.Exists(demoProject))
{
    Console.Error.WriteLine($"Could not find {appDir}.csproj (looked in {demoProject})");
    return 1;
}

Process? server = null;
StaticSiteServer? staticServer = null;
var url = externalUrl;
try
{
    if (string.IsNullOrEmpty(url) && published)
    {
        var wwwroot = PublishApp(demoProject, configuration);
        if (wwwroot == null) return 1;
        staticServer = new StaticSiteServer(wwwroot);
        url = staticServer.Url;
        Console.WriteLine($"serving the published app: {wwwroot} at {url}");
    }
    if (string.IsNullOrEmpty(url))
    {
        (server, url) = await StartServerAsync(demoProject, configuration);
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Dev server did not report an app url");
            return 1;
        }
    }
    // Always request the suite with ?tests=[filter], even when the filter is empty. The Demo is also a
    // scratch host and must not decide for itself whether the suite runs. Empty value runs everything.
    var target = $"{url.TrimEnd('/')}/?tests={Uri.EscapeDataString(filter)}";
    return await RunAsync(target, headed, verbose);
}
finally
{
    if (server != null && !server.HasExited)
    {
        try { server.Kill(entireProcessTree: true); } catch { }
    }
    staticServer?.Dispose();
}

// dotnet publish into a fresh temp folder; returns its wwwroot (null on failure)
static string? PublishApp(string project, string configuration)
{
    var output = Path.Combine(Path.GetTempPath(), "spawnjs-webworkers-published", Path.GetFileNameWithoutExtension(project));
    if (Directory.Exists(output)) Directory.Delete(output, true);
    Console.WriteLine($"publishing {Path.GetFileNameWithoutExtension(project)} [{configuration}] to {output}");
    var psi = new ProcessStartInfo("dotnet", $"publish \"{project}\" -c {configuration} -o \"{output}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    if (p.ExitCode != 0)
    {
        Console.Error.WriteLine($"publish failed ({p.ExitCode}):\n{stdout.Result}\n{stderr.Result}");
        return null;
    }
    return Path.Combine(output, "wwwroot");
}

// walks up from the assembly location to the folder holding the solution
static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0) return dir;
        dir = Path.GetDirectoryName(dir) ?? "";
    }
    return Directory.GetCurrentDirectory();
}

static async Task<(Process?, string)> StartServerAsync(string demoProject, string configuration)
{
    // Say which bits are under test, every run. A Release run exercises the PUBLISHED package, so a
    // passing result says nothing about uncommitted library edits - that has cost real debugging time.
    var testing = configuration == "Debug"
        ? "this working tree (ProjectReference)"
        : "the PUBLISHED package (PackageReference) - local library edits are NOT included";
    Console.WriteLine($"building and starting {Path.GetFileNameWithoutExtension(demoProject)} [{configuration}]");
    Console.WriteLine($"  testing: {testing}");
    var psi = new ProcessStartInfo("dotnet", $"run -c {configuration} --project \"{demoProject}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    var process = Process.Start(psi);
    if (process == null) return (null, "");

    var urlFound = new TaskCompletionSource<string>();
    // the Demo prints "App url: ..."; a Blazor app's dev server prints "Now listening on: ..."
    var appUrl = new Regex(@"(?:App url|Now listening on):\s*(http://\S+)", RegexOptions.IgnoreCase);
    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data == null) return;
        var match = appUrl.Match(e.Data);
        if (match.Success) urlFound.TrySetResult(match.Groups[1].Value);
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    var completed = await Task.WhenAny(urlFound.Task, Task.Delay(TimeSpan.FromMinutes(3)));
    return (process, completed == urlFound.Task ? urlFound.Task.Result : "");
}

static async Task<int> RunAsync(string url, bool headed, bool verbose)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await LaunchAsync(playwright, headed);
    var page = await browser.NewPageAsync();

    var finished = new TaskCompletionSource<string>();
    var results = new List<string>();
    page.Console += (_, msg) =>
    {
        var text = msg.Text;
        if (text.StartsWith("TEST: "))
        {
            results.Add(text.Substring(6));
        }
        else if (text.StartsWith("RESULTS: "))
        {
            finished.TrySetResult(text.Substring(9));
        }
        else if (verbose || msg.Type == "error")
        {
            Console.WriteLine($"  [{msg.Type}] {text}");
        }
    };
    page.PageError += (_, err) => Console.WriteLine($"  [pageerror] {err}");

    Console.WriteLine($"running {url}");
    // Navigate only until the document is parsed - NOT network-idle. A test that spins up a SharedWorker
    // (or anything holding a long-lived connection) never lets the network go idle, so a network-idle wait
    // would time out here even though the suite runs fine. The real completion signal is the page's own
    // "RESULTS:" console line, awaited via finished.Task below.
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
    var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(5)));

    Console.WriteLine();
    var failed = 0;
    foreach (var line in results)
    {
        // Name|Result|DurationMs|Detail
        var parts = line.Split('|', 4);
        if (parts.Length < 3) { Console.WriteLine(line); continue; }
        var mark = parts[1] switch { "Success" => "PASS", "Skipped" => "SKIP", _ => "FAIL" };
        if (mark == "FAIL") failed++;
        Console.WriteLine($"  {mark}  {parts[0]} ({parts[2]}ms)");
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3])) Console.WriteLine($"        {parts[3]}");
    }
    Console.WriteLine();
    if (completed != finished.Task)
    {
        Console.WriteLine("TIMED OUT - the suite never reported a summary");
        return Math.Max(1, failed);
    }
    Console.WriteLine(finished.Task.Result);
    return failed;
}

static async Task<IBrowser> LaunchAsync(IPlaywright playwright, bool headed)
{
    var options = new BrowserTypeLaunchOptions { Headless = !headed };
    try
    {
        // prefer installed Chrome - the bundled chromium build often lags the package version
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = !headed, Channel = "chrome" });
    }
    catch
    {
        return await playwright.Chromium.LaunchAsync(options);
    }
}
