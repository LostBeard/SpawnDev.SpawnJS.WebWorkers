using Microsoft.Extensions.DependencyInjection;
using SpawnDev;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.WebWorkers;
using SpawnDev.SpawnJS.WebWorkers.Demo.Tests;
using System.Net.Http.Json;

// .Net Wasm, unlike Blazor, does not come with a built-in dependency injection container.
// SpawnJSApp is a very minimal DI container that can be used when not using something else.
var builder = SpawnJSAppBuilder.CreateDefault(args);

// register SpawnJSRuntime
builder.Services.AddSpawnJSRuntime(out var JS);

Console.WriteLine($"SpawnJS app: {AppDomain.CurrentDomain.FriendlyName} {JS.GlobalScopeName} {JS.AppBaseUri}");

// register WebWorkerService
builder.Services.AddWebWorkerService();

// HTTPClient set to the app's base address 
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(JS.AppBaseUri) });

// Service used for testing
builder.Services.AddSingleton<IMathsService, MathsService>();

// SpawnJSRunAsync autostarts IBackgroundService and IAsyncBackgroundService services
// and can take a method that runs after all auto-starting services are started
await builder.Build().SpawnJSRunAsync(async (app) =>
{
    // Run the test suite in the window scope only. Workers load this same Program.cs; they must serve as
    // workers, not re-run the suite. The Playwright TestRunner reads the READY/TEST/RESULTS console lines.
    // `?filter=Name` in the url scopes the run. This mirrors the SpawnJS harness.
    if (JS.GlobalScope == GlobalScope.Window)
    {
        async void RunIt_OnClick()
        {
            var httpClient = app.Services.GetRequiredService<HttpClient>();
            var someData = await httpClient.GetFromJsonAsync<string[]>("some-data.json");
            JS.Log("someData", someData);

            // for testing get the WebWorkerService
            var webWorkerService = app.Services.GetRequiredService<WebWorkerService>();
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