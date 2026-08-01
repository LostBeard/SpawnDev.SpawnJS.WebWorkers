using Microsoft.Extensions.DependencyInjection;
using SpawnDev;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.WebWorkers;
using SpawnDev.SpawnJS.WebWorkers.Demo.Tests;
using System;

// .Net Wasm, unlike Blazor, does not come with a built-in dependency injection container.
// SpawnJSApp is a very minimal DI container that can be used when not using something else.
var builder = SpawnJSAppBuilder.CreateDefault(args);

// register SpawnJSRuntime
builder.Services.AddSpawnJSRuntime(out var JS);

// register WebWorkerService
builder.Services.AddWebWorkerService();

// Service used for testing
builder.Services.AddSingleton<IMathsService, MathsService>();

// build
var app = builder.Build();

// This starts IBackgroundService and IAsyncBackgroundService services as needed based on current global scope
await app.Services.StartBackgroundServices();

// Run the test suite in the window scope only. Workers load this same Program.cs; they must serve as
// workers, not re-run the suite. The Playwright TestRunner reads the READY/TEST/RESULTS console lines.
// `?filter=Name` in the url scopes the run. This mirrors the SpawnJS harness.
if (JS.GlobalScope == GlobalScope.Window)
{
    async void RunIt_OnClick()
    {
        // for testing get the WebWorkerService
        var webWorkerService = app.Services.GetRequiredService<WebWorkerService>();
        using var worker = await webWorkerService.GetWebWorker();
        // the below line will print "Hello from Window" to the console from inside
        // the created worker, then the worker will be terminated when disposed via the `using` operator
        await worker!.Run(() => Console.WriteLine($"Hello from {JS.GlobalScopeName}"));
    }
    using var document = JS.GetDocument();
    using var button = document!.QuerySelector<HTMLButtonElement>("#run_it");
    button!.OnClick += RunIt_OnClick;

    await WebWorkerTestSuiteRunner.RunAllAsync(app.Services, WebWorkerTestSuiteRunner.FilterFromLocation());
}

// this keeps this app running until exited via a call to `SpawnJSApp.Exit()`
await app.RunAsync();
