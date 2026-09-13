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
    var bufferSize = 1024 * 1024 * 4;
    var testSize = 100 * 1024 * 1024;
    JS.Set("_runTests", async () =>
    {
        if (JS.IsWindow)
        {
            JS.Log(">> Window OPFS tests");
            {
                //JS.Log(">> OPFSStream test 1 a");
                //{
                //    using var streamZ = await OPFSStream.Open("MyFileAsync1.txt", FileMode.Create);
                //    await AsyncStreamThroughputTester.RunThroughputTestAsync(streamZ, testSize, bufferSize);
                //}
                JS.Log(">> AsyncOPFS async test from Window");
                {
                    await StreamThroughputTester.RunThroughputTestAsync(
                        async () => await OPFSStream.Open("MyFileAsync6.txt", FileMode.Create, FileAccess.Write),
                        async () => await OPFSStream.Open("MyFileAsync6.txt", FileMode.Open, FileAccess.Read),
                        testSize, bufferSize);
                }
                JS.Log(">> OPFSInPlaceStream async test from Window");
                {
                    await StreamThroughputTester.RunThroughputTestAsync(
                        async () => await OPFSInPlaceStream.OpenPath("MyFileAsync11.txt", FileMode.Create, FileAccess.Write),
                        async () => await OPFSInPlaceStream.OpenPath("MyFileAsync11.txt", FileMode.Open, FileAccess.Read),
                        testSize, bufferSize);
                }
            }
            JS.Log("<< OPFSStream test 1");
        }
        else if (JS.IsDedicatedWorkerGlobalScope)
        {
            JS.Log(">> OPFS Stream tests in DedicatedWorkerGlobalScope");
            {
                JS.Log(">> OPFSStreams async test from DedicatedWorkerGlobalScope");
                {
                    await StreamThroughputTester.RunThroughputTestAsync(
                        async () => await OPFSStream.Open("MyFileAsync678978979.txt", FileMode.Create, FileAccess.Write),
                        async () => await OPFSStream.Open("MyFileAsync678978979.txt", FileMode.Open, FileAccess.Read),
                        testSize, bufferSize);
                }
                JS.Log(">> OPFSInPlaceStream async test from DedicatedWorkerGlobalScope");
                {
                    await StreamThroughputTester.RunThroughputTestAsync(
                        async () => await OPFSInPlaceStream.OpenPath("MyFileAsync11789789789.txt", FileMode.Create, FileAccess.Write),
                        async () => await OPFSInPlaceStream.OpenPath("MyFileAsync11789789789.txt", FileMode.Open, FileAccess.Read),
                        testSize, bufferSize);
                }
                JS.Log(">> OPFSInPlaceStream sync test from DedicatedWorkerGlobalScope");
                {
                    using var streamZ = await OPFSInPlaceStream.Open("MyFileSync78978.txt", FileMode.Create, FileAccess.ReadWrite);
                    StreamThroughputTester.RunThroughputTest(
                        () => streamZ, 
                        () => streamZ,
                        testSize, bufferSize);
                }
                JS.Log(">> OPFSStream sync test from DedicatedWorkerGlobalScope");
                {
                    using var streamZ = await OPFSStream.Open("MyFileSync999.txt", FileMode.Create, FileAccess.ReadWrite);
                    StreamThroughputTester.RunThroughputTest(
                        () => streamZ,
                        () => streamZ,
                        testSize, bufferSize);
                }
            }
            JS.Log("<< OPFSStream test 2");
        }
    });

    // Run the test suite in the window scope only. Workers load this same Program.cs; they must serve as
    // workers, not re-run the suite. The Playwright TestRunner reads the READY/TEST/RESULTS console lines.
    // `?filter=Name` in the url scopes the run. This mirrors the SpawnJS harness.
    if (false && JS.GlobalScope == GlobalScope.Window)
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