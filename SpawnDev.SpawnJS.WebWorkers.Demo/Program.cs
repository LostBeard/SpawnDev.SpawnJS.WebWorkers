using Microsoft.Extensions.DependencyInjection;
using SpawnDev;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;
using SpawnDev.SpawnJS.WebWorkers;
using SpawnDev.SpawnJS.WebWorkers.Demo;
using SpawnDev.SpawnJS.WebWorkers.Demo.Tests;
using SpawnDev.SpawnJS.WebWorkers.OPFS;
using System.Net.Http.Json;
using System.Text;

// .Net Wasm, unlike Blazor, does not come with a built-in dependency injection container.
// SpawnJSApp is a very minimal DI container that can be used when not using something else.
var builder = SpawnJSAppBuilder.CreateDefault(args, out var JS);
JS.Verbose = false;

// Verbose enabled debug messages including the marshaller names used for marshalled types
// JS.Verbose = false;

if (JS.Verbose) Console.WriteLine($"SpawnJS app: {AppDomain.CurrentDomain.FriendlyName} {JS.GlobalScopeName} {JS.AppBaseUri}");

// register WebWorkerService
builder.Services.AddWebWorkerService();

// HTTPClient set to the app's base address 
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(JS.AppBaseUri) });

// Service used for testing
builder.Services.AddSingleton<IMathsService, MathsService>();
builder.Services.AddSingleton<IAsyncCallDispatcherTest, AsyncCallDispatcherTest>();

// SpawnJSRunAsync autostarts IBackgroundService and IAsyncBackgroundService services
// and can take a method that runs after all auto-starting services are started
await builder.Build().RunAsync(async (app) =>
{
    var webWorkerService = app.Services.GetRequiredService<WebWorkerService>();
    JS.Log($"Running:: {JS.GlobalScope}");
    JS.Set("_runTests", async () =>
    {
        // Run the test in the window
        await OPFSTest.Run();
        // Run the test in a dedicated worker
        using var worker = await webWorkerService.GetWebWorker();
        await worker!.Run(() => OPFSTest.Run());
    });

    // Run the test suite in the window scope only. Workers load this same Program.cs; they must serve as
    // workers, not re-run the suite. The Playwright TestRunner reads the READY/TEST/RESULTS console lines.
    // `?tests=[Name]` in the url both REQUESTS the suite and scopes it. This mirrors the SpawnJS harness.
    // Driven by the url, never by a literal here, so scratch edits in this file cannot silently disable
    // the suite - see WebWorkerTestSuiteRunner.SuiteRequested.
    if (WebWorkerTestSuiteRunner.SuiteRequested() && JS.GlobalScope == GlobalScope.Window)
    {
        async void RunIt_OnClick()
        {
            // below tests the HTTPClient wit hthe JS.AppBaseUri base address by readign some data that ships with the app
            var httpClient = app.Services.GetRequiredService<HttpClient>();
            var someData = await httpClient.GetFromJsonAsync<string[]>("some-data.json");
            JS.Log("someData", someData);

            // for testing get the WebWorkerService

            // below will switcxh Blob worker loadign from auto to forced (for testing)
            // Blob workers are used when loaded from a CDN (cross origin loaded)
            //webWorkerService.ForceBlobWorkers = true;

            // get a new dedicated worker that is destroyed when disposed
            using var worker = await webWorkerService.GetWebWorker();
            // the below line will print "Hello from Window" to the console from inside
            // the created worker, then the worker will be terminated when disposed via the `using` operator
            await worker!.Run(() => Console.WriteLine($"Hello from {JS.GlobalScopeName}"));
        }

        // Create a button and insert add it to body with a click handler pointed at RunIt_OnClick
        using var document = JS.GetDocument();
        using var button = document!.CreateElement<HTMLButtonElement>("button");
        button.InnerText = "Run Worker";
        using var body = document!.Body;
        body!.Append(button);
        button!.OnClick += RunIt_OnClick;

        // Test tunner
        await WebWorkerTestSuiteRunner.RunAllAsync(app.Services, WebWorkerTestSuiteRunner.FilterFromLocation());
    }
});

public static class OPFSTest
{
    static SpawnJSRuntime JS => SpawnJSRuntime.Instance;
    public static async Task Run()
    {
        using var navigator = JS.Get<Navigator>("navigator");
        using var storage = navigator.Storage;
        using var root = await storage.GetDirectory();
        var bufferSize = 1024 * 1024 * 4;
        var testSize = 100 * 1024 * 1024;

        JS.Log($">> OPFSStream async test from {JS.GlobalScope}");
        {
            var filename = "MyFileAsync6.txt";
            await StreamThroughputTester.RunThroughputTestAsync(
                async () => await OPFSStream.Open(filename, FileMode.Create, FileAccess.Write),
                async () => await OPFSStream.Open(filename, FileMode.Open, FileAccess.Read),
                testSize, bufferSize);

            try { await root.RemoveEntry(filename); } catch { }
        }
        if (JS.IsDedicatedWorkerGlobalScope)
        {
            JS.Log($">> OPFSStream sync test from {JS.GlobalScope}");
            {
                var filename = "MyFileAsync67.txt";
                await StreamThroughputTester.RunThroughputTest(
                    async () => await OPFSStream.Open(filename, FileMode.Create, FileAccess.Write, OPFSFileOptions.SyncRequired),
                    async () => await OPFSStream.Open(filename, FileMode.Open, FileAccess.Read, OPFSFileOptions.SyncRequired),
                    testSize, bufferSize);

                try { await root.RemoveEntry(filename); } catch { }
            }
        }
    }
}