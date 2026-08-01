using Microsoft.Playwright;
using System.Diagnostics;
using System.Text.RegularExpressions;

// SpawnDev.SpawnJS.WebWorkers test harness (a port of the SpawnJS harness).
//
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner                      run everything
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- Serialization     run tests whose name contains "Serialization"
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --headed          watch it in a real browser window
//   dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --url http://...  use an already running dev server
//
// Exit code is the number of failed tests, so it is usable as a gate.

var filter = "";
var headed = false;
var externalUrl = "";
var verbose = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--headed": headed = true; break;
        case "--verbose": verbose = true; break;
        case "--url": externalUrl = ++i < args.Length ? args[i] : ""; break;
        case "--filter": filter = ++i < args.Length ? args[i] : ""; break;
        case "-h":
        case "--help":
            Console.WriteLine("usage: [filter] [--filter <text>] [--headed] [--verbose] [--url <url>]");
            return 0;
        default:
            if (!args[i].StartsWith("-")) filter = args[i];
            break;
    }
}

var repoRoot = FindRepoRoot();
var demoProject = Path.Combine(repoRoot, "SpawnDev.SpawnJS.WebWorkers.Demo", "SpawnDev.SpawnJS.WebWorkers.Demo.csproj");
if (!File.Exists(demoProject))
{
    Console.Error.WriteLine($"Could not find SpawnDev.SpawnJS.WebWorkers.Demo.csproj (looked in {demoProject})");
    return 1;
}

Process? server = null;
var url = externalUrl;
try
{
    if (string.IsNullOrEmpty(url))
    {
        (server, url) = await StartServerAsync(demoProject);
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Dev server did not report an app url");
            return 1;
        }
    }
    var target = string.IsNullOrEmpty(filter) ? url : $"{url.TrimEnd('/')}/?filter={Uri.EscapeDataString(filter)}";
    return await RunAsync(target, headed, verbose);
}
finally
{
    if (server != null && !server.HasExited)
    {
        try { server.Kill(entireProcessTree: true); } catch { }
    }
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

static async Task<(Process?, string)> StartServerAsync(string demoProject)
{
    Console.WriteLine("building and starting SpawnDev.SpawnJS.WebWorkers.Demo...");
    var psi = new ProcessStartInfo("dotnet", $"run -c Release --project \"{demoProject}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    var process = Process.Start(psi);
    if (process == null) return (null, "");

    var urlFound = new TaskCompletionSource<string>();
    var appUrl = new Regex(@"App url:\s*(http://\S+)", RegexOptions.IgnoreCase);
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
