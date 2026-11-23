using System.Diagnostics;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.WebWorkers;

namespace SpawnDev.SpawnJS.WebWorkers.DemoBlazor;

/// <summary>
/// The Blazor demo's self-tests, for the TestRunner (`--app SpawnDev.SpawnJS.WebWorkers.DemoBlazor`): run when the page is opened with
/// ?tests=, report each test as "TEST: name|result|ms|detail" and finish with "RESULTS: ..." on the console (the runner's protocol, as
/// in the non-Blazor Demo). They cover what only a BLAZOR app exercises: a classic-mode worker boots the Blazor runtime through the
/// module patcher (spawndev.spawnjs.webworkers.js fixModuleScript), which the non-Blazor Demo never loads.
/// </summary>
public static class WorkerSelfTests
{
    public static async Task RunAsync(WebWorkerService workers)
    {
        int passed = 0, failed = 0;
        async Task Test(string name, Func<Task> body)
        {
            var sw = Stopwatch.StartNew();
            string result = "Success", detail = "";
            try
            {
                var run = body();
                if (await Task.WhenAny(run, Task.Delay(30000)) != run) throw new TimeoutException("no answer within 30 s (did the worker boot?)");
                await run;
            }
            catch (Exception ex) { result = "Error"; detail = ex.Message; }
            if (result == "Success") passed++; else failed++;
            Console.WriteLine($"TEST: {name}|{result}|{sw.ElapsedMilliseconds}|{detail.Replace('|', '/').Replace('\n', ' ')}");
        }

        // The app's JS initializer (wwwroot/SpawnDev.SpawnJS.WebWorkers.DemoBlazor.lib.module.js) calls its own exported function when
        // it loads. In a worker the module patcher must keep that working: it turned exported functions into expressions, the call threw
        // "countInitializerRun is not defined" and the worker never booted (2.2.7; found with SpawnDev.Blazor.BootRecovery 1.0.0).
        await Test("WorkerAppInitializerCallingItsOwnExportRuns", async () =>
        {
            using var worker = await workers.GetWebWorker();
            if (worker == null) throw new Exception("web workers are not supported here");
            var runs = await worker.Run(() => InitializerRunCount());
            if (runs < 1) throw new Exception($"the app's JS initializer did not run in the worker (runs: {runs})");
        });

        Console.WriteLine($"RESULTS: {passed} passed, {failed} failed");
    }

    /// <summary>Runs IN the worker: how many times the app's JS initializer counted itself in this scope.</summary>
    public static int InitializerRunCount() => SpawnJSRuntime.Instance.Get<int?>("__webWorkersDemoInitializerRuns") ?? 0;
}
