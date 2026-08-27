# SpawnDev.SpawnJS.WebWorkers
[![NuGet](https://img.shields.io/nuget/dt/SpawnDev.SpawnJS.WebWorkers.svg?label=SpawnDev.SpawnJS.WebWorkers)](https://www.nuget.org/packages/SpawnDev.SpawnJS.WebWorkers)  

> 💜 **Built and maintained by one independent developer** — no company, no overhead, just code. If SpawnDev.SpawnJS.WebWorkers saves you time, please consider [**sponsoring its development »**](https://github.com/sponsors/LostBeard). Sponsorship is what keeps it alive and maintained.

Run full .Net WebAssembly services in Web Workers, Shared Web Workers, and Service Workers, with transparent cross-thread method invocation - built on [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS).

- Call services in separate threads with WebWorkers and SharedWebWorkers
- Call services in other Windows
- Add and remove services at runtime ([Runtime Services](#runtime-services))
- Supports keyed services
- Create a new instance of a class and add it as a runtime service using a `new` expression
- [TaskPool](#webworkerservicetaskpool) support via WebWorkers
- Call your own private static methods in background threads (supports service injection)
- Supports method parameter service injection via `[FromServices]` and `[FromKeyedServices]` parameter attributes
- Works in .Net WASM 10
- SharedArrayBuffer is not required. No special HTTP headers to configure.
- Supports [transferable objects](#transferable-objects)
- Run .Net WASM in a ServiceWorker

## SpawnJS.WebWorkers vs BlazorJS.WebWorkers

SpawnDev.SpawnJS.WebWorkers is the [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS) port of [SpawnDev.BlazorJS.WebWorkers](https://github.com/LostBeard/SpawnDev.BlazorJS.WebWorkers). The API is nearly identical, but there are important differences:

- **It targets plain .Net WASM, not Blazor.** SpawnJS provides Javascript interop for .Net WebAssembly without Blazor and without the JSON serialization layer - references are held as integer slots rather than `JSObject` proxies. There is no `WebAssemblyHostBuilder`, `RootComponents`, or `BlazorJSRunAsync()`.
- **.Net WASM has no built-in dependency injection container**, so SpawnJS ships a minimal one (`SpawnJSAppBuilder`). You can use it, or wire your own.
- **Your app is bundled to run in workers.** SpawnJS.WebWorkers builds two extra JavaScript entrypoints (`main.classic.js` and `main.module.js`) from your app's own output so it can run as a classic or module Worker/SharedWorker/ServiceWorker. This needs `WasmBundlerFriendlyBootConfig` (set automatically) and Node.js on PATH at build/publish. See [Worker bundle](#worker-bundle) below.

If you are using Blazor WASM, use [SpawnDev.BlazorJS.WebWorkers](https://github.com/LostBeard/SpawnDev.BlazorJS.WebWorkers) instead.

### Supported .Net Versions
- .Net 10
- .Net WebAssembly Standalone App (`Microsoft.NET.Sdk.WebAssembly`)

Tested working in the following browsers. Note that Chrome on Android does not currently support SharedWorkers.

| Browser  | OS         | WebWorker | SharedWebWorker |
|----------|------------|-----------|-----------------|
| Chrome   | Windows    | ✔ | ✔ |
| MS Edge  | Windows    | ✔ | ✔ |
| Firefox  | Windows    | ✔ | ✔ |
| Chrome   | Android    | ✔ | ❌ (SharedWorker not supported by browser) |
| Firefox  | Android    | ✔ | ✔ |

If you have ***ANY*** issues or questions please open an issue [here](https://github.com/LostBeard/SpawnDev.SpawnJS.WebWorkers/issues) on GitHub.

## Worker bundle

> **New in 1.0.0** - workers now load a **classic or module bundle** built from your app's own output (`main.classic.js` / `main.module.js`), replacing the old module-only worker script. This is what makes the app runnable as a classic `<script>` / `importScripts()` and in browser-extension scopes. See [Docs/build-properties.md](Docs/build-properties.md) for the MSBuild properties (including the publish-only browser-extension folder rename).

SpawnDev.SpawnJS.WebWorkers builds two extra JavaScript entrypoints from your app's own output and uses them to run your .Net WASM app in a Worker, SharedWorker, or ServiceWorker:

| Entrypoint | Kind | Used for |
|---|---|---|
| `main.js` | your app default (untouched) | not used directly once the app is bundler-friendly (see below) |
| `main.classic.js` | classic (non-module) | **default** for new Worker/SharedWorker/ServiceWorker; also loadable via plain `<script src>` or `importScripts()` |
| `main.module.js` | ES module | recommended page entrypoint; used when a module worker is explicitly requested |

Both bundled entrypoints reference your app's existing `_framework` output as-is - **no assets are duplicated, only the two JS files are added** - and fold in the event-holder that captures early ServiceWorker/SharedWorker events while .Net boots.

See [Docs/build-properties.md](Docs/build-properties.md) for the full list of MSBuild properties (`SpawnJSWebWorkersClassicBundle`, `SpawnJSWebWorkersFrameworkFolderName`, `SpawnJSWebWorkersContentFolderName`, …).

### Requirements

- **Node.js on PATH** at build and publish. The bundle is produced by an offline, self-contained Rollup toolchain that runs under Node (no `npm install`, no network).
- **`WasmBundlerFriendlyBootConfig=true`** - set automatically by this package. The .Net WASM boot config must use static (bundler-followable) imports so the bundle can be produced from, and reference, your app's own `_framework`. Per the .Net SDK docs this output is not meant to be loaded directly by the browser, so your app boots through the bundle instead of the raw `main.js`.

### Wire your index.html to the bundle

Because the app boots through the bundle, point your `index.html` module script at `main.module.js`:

```html
<!-- instead of the default main.js / main#[.{fingerprint}]!.js -->
<script type="module" src="main.module.js"></script>
```

### Opting out

Set `<SpawnJSWebWorkersClassicBundle>false</SpawnJSWebWorkersClassicBundle>` to skip the bundle build. The app is then a normal (non-bundler-friendly) .Net WASM app and worker creation falls back to the legacy module worker script (`spawndev.spawnjs.webworkers.module.js`), which only works when asset fingerprinting is off.

### Browser extensions - renaming `_framework`

Because the classic bundle (`main.classic.js`) can be loaded via a plain `<script src>` or `importScripts()` and reuses the app's own `_framework` output, it makes a great runtime for a **browser extension** background ServiceWorker, content scripts, and other extension scopes. One obstacle: a browser extension cannot have root files or folders whose names start with `_` (a leading `.` is likewise unsafe), so the default `_framework` (and `_content`, if your app uses Razor Class Library static assets) folders are illegal at the extension root.

Two **publish-only, opt-in** properties rename those folders and rewrite every reference to them in the published output (including `main.classic.js` / `main.module.js`, `index.html`, and the boot config):

```xml
<PropertyGroup>
  <!-- rename wwwroot/_framework -> wwwroot/framework on publish -->
  <SpawnJSWebWorkersFrameworkFolderName>framework</SpawnJSWebWorkersFrameworkFolderName>
  <!-- only if your app has a _content folder (RCL static assets) -->
  <SpawnJSWebWorkersContentFolderName>content</SpawnJSWebWorkersContentFolderName>
</PropertyGroup>
```

Notes:
- **Publish-only.** Normal builds and `dotnet run` are untouched (the folders keep their default names in dev).
- The new name must not start with `_` or `.` (that would defeat the purpose) and must be a single folder name.
- This is a sharp tool: it rewrites references it can see in the published `.js`/`.html`/`.json`/`.css`. If your app builds a `"_framework"`/`"_content"` path at runtime from a variable, that reference will not be rewritten. Only opt in if you understand your app's asset loading.
- You will still typically place the app under its own subfolder (e.g. `app/`) with the extension `manifest.json` at the extension root; the rename removes the remaining underscore-prefixed paths inside the app folder.

## Example setup and usage

`Program.cs`
```cs
using Microsoft.Extensions.DependencyInjection;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.WebWorkers;

// .Net WASM, unlike Blazor, does not come with a built-in dependency injection container.
// SpawnJSAppBuilder is a very minimal DI container that can be used when not using something else.
var builder = SpawnJSAppBuilder.CreateDefault(args);

// register SpawnJSRuntime (JS is the SpawnJSRuntime instance)
builder.Services.AddSpawnJSRuntime(out var JS);

// register WebWorkerService
// Use defaults (PoolSize = 0, MaxPoolSize = 1, AutoGrow = true):
// builder.Services.AddWebWorkerService();
// Or configure:
builder.Services.AddWebWorkerService(webWorkerService =>
{
    // Default WebWorkerService.TaskPool settings: PoolSize = 0, MaxPoolSize = 1, AutoGrow = true
    // Setting MaxPoolSize to -1 sets it to navigator.hardwareConcurrency
    webWorkerService.TaskPool.MaxPoolSize = 2;
    // Start 2 TaskPool workers if running in a Window scope, 0 otherwise
    webWorkerService.TaskPool.PoolSize = webWorkerService.GlobalScope == GlobalScope.Window ? 2 : 0;
});

// Your services
builder.Services.AddSingleton<IMathService, MathService>();

var app = builder.Build();

// Starts IBackgroundService / IAsyncBackgroundService services as needed for the current global scope
await app.Services.StartBackgroundServices();

// Keeps the app running until SpawnJSApp.Exit() is called
await app.RunAsync();
```

`wwwroot/main.js`
```js
import { dotnet } from './_framework/dotnet.js'
const { runMain } = await dotnet.withApplicationArguments("start").create();
await runMain();
```

The same `Program.cs` runs in **every** scope - Window, Worker, SharedWorker, and ServiceWorker. Each scope is a complete, separate instance of your .Net WASM app.

## WebWorkerService
The `WebWorkerService` singleton contains many methods for working with multiple instances of your app running in any scope, whether Window, Worker, SharedWorker, or ServiceWorker.

### Primary WebWorkerService members:
- **Info** - basic info about the currently running instance, like instance id and the global scope type.
- [**TaskPool**](#webworkerservicetaskpool) - a `WebWorkerPool` that gives quick and easy access to any number of app instances running in dedicated worker threads. TaskPool threads can be started at startup or on demand.
- [**WindowTask**](#webworkerservicewindowtask) - dispatches on the current Window scope, or on the parent Window's scope when the current scope is a Worker created by a Window. Only available in a Window context, or in a Worker created by a Window.
- [**Instances**](#webworkerserviceinstances) - access to every running instance of your app in the active browser, across every scope (other Windows, Workers, SharedWorkers, and ServiceWorkers). Call directly into any running instance from any instance.
- [**Locks**](#webworkerservicelocks) - an instance of `LockManager` acquired from `navigator.locks`. Provides cross-thread locks in all browser scopes, similar to a .Net Mutex.
- [**GetWebWorker**](#webworker) - async method that creates and returns a new `WebWorker` when it is ready.
- [**GetSharedWebWorker**](#sharedwebworker) - async method that returns a `SharedWebWorker` with the given name, accessible by all instances. Created if it does not already exist.

### Notes and Common Issues or Questions
- WebWorkers are separate instances of your .Net WASM app running in [Workers](https://developer.mozilla.org/en-US/docs/Web/API/Worker). These instances are called into using [postMessage](https://developer.mozilla.org/en-US/docs/Web/API/Worker/postMessage).

#### Why does the developer console show more than one startup message?
- Those messages are from WebWorkers starting up. Dedicated workers share the window's console, so startup and other console messages from them are normal. Note: SharedWebWorkers do **not** share console logs with the window that created them. See [Important Note About SharedWebWorker](#important-note-about-sharedwebworker).

#### When I change a static variable in a Window it does not change in a worker. Why?
- Each worker loads the app as a separate instance, to allow running code in the background. This is more like starting multiple copies of an app and using inter-process communication than starting separate threads in the same app. Static variables are not shared, but can be accessed asynchronously using [Expressions](#expression-examples).

## AsyncCallDispatcher
`AsyncCallDispatcher` is the base class used for accessing other instances of your app. It provides a few different calling conventions for instance-to-instance communication.

Where `AsyncCallDispatcher` is used:
- `AppInstance`, [`WebWorker`](#webworker), [`SharedWebWorker`](#sharedwebworker), and `WebWorkerPool` all inherit from `AsyncCallDispatcher`.
- [`WebWorkerService.TaskPool`](#webworkerservicetaskpool) - a `WebWorkerPool`, which inherits from `AsyncCallDispatcher`.
- [`WebWorkerService.WindowTask`](#webworkerservicewindowtask) - an `AsyncCallDispatcher`.
- [`WebWorkerService.Instances`](#webworkerserviceinstances) - a `List<AppInstance>`; `AppInstance` inherits from `AsyncCallDispatcher`.

### Runtime Services
Keyed services, adding and removing services at runtime, and runtime service creation using a `new` expression are all supported. These make workers easier to use without pre-registering every class used in a worker.

- Add a service at runtime
  `await worker.AddService<SomeClass>();`
  `await worker.AddService<ISomeClass, SomeClass>();`
- Remove a service added at runtime
  `await worker.RemoveService<ISomeClass>();`
- Check if a service exists
  `bool exists = await worker.ServiceExists<ISomeClass>();`

### Supported Instance-To-Instance Calling Conventions

**Expressions** - `Run()`, `Set()`, `New()`
- Supports keyed services.
- Supports generics, property get and set, and asynchronous and synchronous method calls.
- Supports creating new instances of keyed and non-keyed services at runtime.
- Supports calling private methods from inside the owning class.

#### Expression examples
- Property Set using `Set<TService, TProperty>()`
  `await worker.Set<SomeService, string>(s => s.SomeProperty, "new property value");`
- Property Get using `Run<TService, TProperty>()`
  `string value = await worker.Run<SomeService, string>(s => s.SomeProperty);`
- Method call using `Run<TService, TReturn>()`
  `string result = await worker.Run<SomeService, string>(s => s.SomeMethod("some data"));`
- Create a new instance and register it as a service
  `await worker.New(() => new SomeClass("some init var"));`
- Create a new instance and specify the service type to register it as
  `await worker.New<ISomeRendererClass>(() => new SomeRendererClass(offscreenCanvas));`

**Delegates** - `Invoke()`
- Supports generics, and asynchronous and synchronous method calls.
- Supports calling private methods from inside the owning class.
- Method groups only - lambdas do not work with `Invoke()`.

**Interface proxy** - `GetService()`
- Requires services to be registered using an interface.
- Supports keyed services.
- Supports generics and asynchronous method calls.
- Does not support static methods, private methods, synchronous calls, or properties.

Example that demonstrates using Expression, Delegate, and Interface proxy invokers to call service methods in a TaskPool WebWorker.

```cs
public interface IMyService
{
    Task<string> WorkerMethodAsync(string input);
}
public class MyService : IMyService
{
    WebWorkerService WebWorkerService;
    public MyService(WebWorkerService webWorkerService)
    {
        WebWorkerService = webWorkerService;
    }
    private string WorkerMethod(string input)
    {
        return $"Hello {input} from {WebWorkerService.InstanceId}";
    }
    public async Task<string> WorkerMethodAsync(string input)
    {
        return $"Hello {input} from {WebWorkerService.InstanceId}";
    }
    public async Task CallWorkerMethod()
    {
        // Call the private method on this scope (normal, local call)
        Console.WriteLine(WorkerMethod(WebWorkerService.InstanceId));

        // Call a private synchronous method in a WebWorker thread using a Delegate
        Console.WriteLine(await WebWorkerService.TaskPool.Invoke(WorkerMethod, WebWorkerService.InstanceId));

        // Call a private synchronous method in a WebWorker thread using an Expression
        Console.WriteLine(await WebWorkerService.TaskPool.Run(() => WorkerMethod(WebWorkerService.InstanceId)));

        // Call a public async method in a WebWorker thread using an Expression
        Console.WriteLine(await WebWorkerService.TaskPool.Run<IMyService, string>(s => s.WorkerMethodAsync(WebWorkerService.InstanceId)));

        // Call a public async method in a WebWorker thread using an Interface Proxy
        var service = WebWorkerService.TaskPool.GetService<IMyService>();
        Console.WriteLine(await service.WorkerMethodAsync(WebWorkerService.InstanceId));
    }
}
```

## WebWorkerService.TaskPool
`WebWorkerService.TaskPool` is ready to call any registered service in a background thread. If WebWorkers are not supported, TaskPool calls run in the Window scope. TaskPool settings are configured when calling `AddWebWorkerService()`. By default no worker tasks are started automatically at startup and the max pool size is 1. See the setup example above.

## WebWorkerService.Instances
`WebWorkerService.Instances` is a `List<AppInstance>` where each item represents a running instance. `AppInstance` provides basic information about the running instance and lets you call into it via its base class [`AsyncCallDispatcher`](#asynccalldispatcher).

```cs
// Get an AppInstance for each instance running in a Window global scope
var windowInstances = WebWorkerService.Instances.Where(o => o.Info.Scope == GlobalScope.Window).ToList();
var localInstanceId = WebWorkerService.InstanceId;
foreach (var windowInstance in windowInstances)
{
    // Read a property from another instance (here the SpawnJSRuntime InstanceId)
    var remoteInstanceId = await windowInstance!.Run(() => JS.InstanceId);
    // Call a method (here the static method Console.WriteLine) in another instance
    await windowInstance.Run(() => Console.WriteLine("Hello " + remoteInstanceId + " from " + localInstanceId));
}
```

## WebWorkerService.WindowTask
Workers sometimes need to call back into the Window thread that owns them. Use `WebWorkerService.WindowTask`. If the current scope is a Window it dispatches on the current scope; if the current scope is a Worker created by a Window it dispatches on the parent Window's scope.

```cs
public class MyService
{
    WebWorkerService WebWorkerService;
    public MyService(WebWorkerService webWorkerService)
    {
        WebWorkerService = webWorkerService;
    }
    string CalledOnWindow(string input)
    {
        return $"Hello {input} from {WebWorkerService.InstanceId}";
    }
    public async Task StartedInWorker()
    {
        // Report back to the Window using an Expression
        Console.WriteLine(await WebWorkerService.WindowTask.Run(() => CalledOnWindow(WebWorkerService.InstanceId)));

        // Report back to the Window using a Delegate
        Console.WriteLine(await WebWorkerService.WindowTask.Invoke(CalledOnWindow, WebWorkerService.InstanceId));
    }
}
```

### Using SharedCancellationToken to cancel a WebWorker task
`SharedCancellationToken` is a supported parameter type and can be used to cancel a running task. It works similarly to `CancellationToken`, and `SharedCancellationTokenSource` works similarly to `CancellationTokenSource`.

```cs
public async Task WebWorkerSharedCancellationTokenTest()
{
    if (!WebWorkerService.WebWorkerSupported) throw new Exception("Worker not supported by browser.");
    // Cancel the task after 2 seconds
    using var cts = new SharedCancellationTokenSource(2000);
    var i = await WebWorkerService.TaskPool.Run(() => CancellableMethod(10000, cts.Token));
    if (i == -1) throw new Exception("Task Cancellation failed");
}

// Returns -1 if not cancelled. Runs for up to 10 seconds if not cancelled.
private static async Task<long> CancellableMethod(double maxRunTimeMS, SharedCancellationToken token)
{
    var startTime = DateTime.Now;
    var maxRunTime = TimeSpan.FromMilliseconds(maxRunTimeMS);
    long i = 0;
    while (DateTime.Now - startTime < maxRunTime)
    {
        i += 1;
        if (token.IsCancellationRequested) return i;
    }
    return -1;
}
```

#### Limitation: SharedCancellationToken requires cross-origin isolation
`SharedCancellationToken` and `SharedCancellationTokenSource` use a `SharedArrayBuffer` for signaling instead of postMessage. This lets them work in both synchronous and asynchronous methods, but requires cross-origin isolation (COOP/COEP) due to `SharedArrayBuffer` restrictions.

### Using CancellationToken to cancel a WebWorker task
`CancellationToken` is a supported parameter type and can be used to cancel a running task.

```cs
public async Task TaskPoolExpressionWithCancellationTokenTest()
{
    if (!WebWorkerService.WebWorkerSupported) throw new Exception("Worker not supported by browser.");
    // Cancel the task after 2 seconds
    using var cts = new CancellationTokenSource(2000);
    var cancelled = await WebWorkerService.TaskPool.Run(() => CancellableMethod(10000, cts.Token));
    if (!cancelled) throw new Exception("Task Cancellation failed");
}

// Returns true if cancelled. Runs for up to 10 seconds if not cancelled.
private static async Task<bool> CancellableMethod(double maxRunTimeMS, CancellationToken token)
{
    var startTime = DateTime.Now;
    var maxRunTime = TimeSpan.FromMilliseconds(maxRunTimeMS);
    while (DateTime.Now - startTime < maxRunTime)
    {
        await Task.Delay(50);
        if (await token.IsCancellationRequestedAsync()) return true;
    }
    return false;
}
```

#### Limitation: CancellationToken requires the receiving method to be async
When a `CancellationTokenSource` cancels a token passed to a WebWorker, a postMessage is sent to the worker(s) to notify them. The method using the `CancellationToken` must yield the thread briefly (`await Task.Delay(1)`) so the message event handler can receive the cancellation before rechecking the token. The extension methods `CancellationToken.IsCancellationRequestedAsync()` and `CancellationToken.ThrowIfCancellationRequestedAsync()` do this internally. Therefore `CancellationToken` will not work in a synchronous method. `SharedCancellationToken` does not have this limitation.

## WebWorkerService.Locks
`WebWorkerService.Locks` is an instance of [LockManager](https://developer.mozilla.org/en-US/docs/Web/API/LockManager) acquired from `navigator.locks`. The MDN documentation explains the interface and has examples.

### WebWorkerService.Locks.Request()
```cs
public async Task SynchronizeDatabase()
{
    JS.Log("requesting lock");
    await WebWorkerService.Locks.Request("my_lock", async (lockInfo) =>
    {
        // Because this is an exclusive lock, this callback never runs in more than 1 thread at a time.
        JS.Log("have lock", lockInfo);
        await Task.Delay(1000); // simulate async work
        JS.Log("releasing lock"); // the lock is not released until this async method exits
    });
    JS.Log("released lock");
}
```

### WebWorkerService.Locks.RequestHandle()
`LockManager.RequestHandle()` is an extension method that, instead of taking a callback, waits for the lock and returns a `TaskCompletionSource` used to release it. It is more convenient when a lock must be held for an extended period.

```cs
public async Task SynchronizeDatabase()
{
    JS.Log("requesting lock");
    TaskCompletionSource tcs = await WebWorkerService.Locks.RequestHandle("my_lock");
    // Because this is an exclusive lock, the code up to tcs.SetResult() never runs in more than 1 thread at a time.
    JS.Log("have lock");
    await Task.Delay(1000); // simulate async work
    JS.Log("releasing lock");
    tcs.SetResult();
    JS.Log("released lock");
}
```

## WebWorker
Use `WebWorkerService.SharedWebWorkerSupported` and `WebWorkerService.WebWorkerSupported` to check for support.

Simple fallback when not supported:
- If `WebWorkerService.GetWebWorker()` returns a `WebWorker`, use `WebWorker.GetService<T>()`.
- If `WebWorkerService.GetWebWorker()` returns null, resolve the service from your own DI container instead.

```cs
// Create a WebWorker
var webWorker = await workerService.GetWebWorker();

// GetService<TInterface> returns a proxy for the service on the worker. Interfaces only.
var workerMathService = webWorker.GetService<IMathService>();

// Call async methods on the worker's service
var result = await workerMathService.CalculatePi(piDecimalPlaces);

// Action types can be passed for progress reporting (Func is not currently supported)
var result2 = await workerMathService.CalculatePiWithActionProgress(piDecimalPlaces, new Action<int>((i) =>
{
    piProgress = i;
    // update UI ...
}));
```

## SharedWebWorker
Calling `GetSharedWebWorker` with the same name (from the same or another window) returns the same SharedWebWorker.

```cs
// Create or get the SharedWebWorker with the provided name
var sharedWebWorker = await workerService.GetSharedWebWorker("workername");

// Just like WebWorker, but shared
var workerMathService = sharedWebWorker.GetService<IMathService>();
var result = await workerMathService.CalculatePi(piDecimalPlaces);
```

### Important Note About SharedWebWorker
SharedWebWorkers do not share console logs with the window that created them. This is a browser limitation. To view the output from a SharedWebWorker in Chrome, open `chrome://inspect/#workers` and find the SharedWebWorker instance.

## Send events
You can send and receive events between connected workers using the `OnMessage` event and `SendEvent()` method.

```cs
// Listen for event messages from the worker
worker.OnMessage += (ServiceCallDispatcher sender, string eventName, Array data) =>
{
    if (eventName == "progress")
    {
        // Read the event data from the data Array (if any)
        PiProgress msgData = data.Shift<PiProgress>();
        // update UI ...
    }
};
```

### Send an event from a worker owner to the worker
```cs
webWorker.SendEvent("progress", new[] { new PiProgress { Progress = piProgress } });
```

### Send an event from a SharedWebWorker or WebWorker to its connected parent(s)
```cs
webWorkerService.SendEventToParents("progress", new[] { new PiProgress { Progress = piProgress } });
```

## Transferable Objects
Data passed between the main thread and a worker goes through `postMessage()`, which copies by default - slow for large data sets. Some objects can be *transferred* instead of copied. When an object is transferred, ownership moves to the receiving thread and the sending thread can no longer access it.

- See MDN on [Transferable objects](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Transferable_objects).

### WorkerTransferAttribute
- `WorkerTransfer` can be applied to method parameters and return values to modify the default transfer-list behavior.
- When omitted, `[WorkerTransfer(WorkerTransferMode.TransferRequired, Depth = 3)]` is used - only Transferable objects that require transfer are added to the transfer list.
- `[WorkerTransfer]` is equivalent to `[WorkerTransfer(WorkerTransferMode.TransferAll, Depth = 3)]` - all Transferable objects are added to the transfer list.
- `Depth` controls how deep the transfer check descends into objects (default 3).

### WorkerTransferMode enum
- `TransferRequired` - only objects that are transferable and require transfer are added (e.g. OffscreenCanvas).
- `TransferAll` - all transferable objects are added, even if they do not require transfer.
- `TransferNone` - nothing is added, even if transferable.

```cs
[return: WorkerTransfer]
public async Task<ImageBitmap> ProcessFrame([WorkerTransfer] ArrayBuffer frameBuffer, int width, int height)
{
    // ... process the input ArrayBuffer
    ImageBitmap ret = /* ... */;
    return ret;
}
```

### TransferableListAttribute
- Marks a single method parameter as an explicit transfer list.
- Use on a single parameter whose type implements `IEnumerable<object>`.
- If `TransferableList` is used, `WorkerTransfer` on other parameters is ignored.
- The received `TransferableList` parameter is null in the worker - it only indicates what to transfer.

```cs
private static async Task<OffscreenCanvas> ProcessOnWorker(OffscreenCanvas offscreenCanvas, [TransferableList] object[] transferList)
{
    return offscreenCanvas;
}
```

### Transferable JSObject types (source: [MDN](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Transferable_objects#supported_objects))
`ArrayBuffer`, `AudioData`, `ImageBitmap`, `MediaSourceHandle`, `MediaStreamTrack`, `MessagePort`, `MIDIAccess`, `OffscreenCanvas`, `ReadableStream`, `RTCDataChannel`, `TransformStream`, `VideoFrame`, `WebTransportReceiveStream`, `WebTransportSendStream`, `WritableStream`.

## ServiceWorker
SpawnDev.SpawnJS.WebWorkers supports running in a ServiceWorker. Register a class to run in the ServiceWorker to handle events.

### Program.cs
```cs
var builder = SpawnJSAppBuilder.CreateDefault(args);

// SpawnJS
builder.Services.AddSpawnJSRuntime(out var JS);

// WebWorkers
builder.Services.AddWebWorkerService();

// Register a ServiceWorker handler (inherits from ServiceWorkerEventHandler)
builder.Services.RegisterServiceWorker<AppServiceWorker>();

// Or unregister the ServiceWorker if no longer desired
// builder.Services.UnregisterServiceWorker();

var app = builder.Build();
await app.Services.StartBackgroundServices();
await app.RunAsync();
```

### AppServiceWorker.cs
Handle ServiceWorker events by overriding the `ServiceWorkerEventHandler` base class virtual methods. The handlers are only called when running in a `ServiceWorkerGlobalScope`. The singleton may start in any scope, so it must be scope aware.

```cs
public class AppServiceWorker : ServiceWorkerEventHandler
{
    public AppServiceWorker(SpawnJSRuntime js) : base(js) { }

    // Called before any ServiceWorker events are handled, in whatever scope the app starts in.
    protected override async Task OnInitializedAsync()
    {
        Log("GlobalThisTypeName", JS.GlobalThisTypeName);
    }

    protected override async Task ServiceWorker_OnInstallAsync(ExtendableEvent e)
    {
        Log("ServiceWorker_OnInstallAsync");
        _ = ServiceWorkerThis!.SkipWaiting();
    }

    protected override async Task ServiceWorker_OnActivateAsync(ExtendableEvent e)
    {
        Log("ServiceWorker_OnActivateAsync");
        await ServiceWorkerThis!.Clients.Claim();
    }

    protected override async Task<Response> ServiceWorker_OnFetchAsync(FetchEvent e)
    {
        try
        {
            return await JS.Fetch(e.Request);
        }
        catch (Exception ex)
        {
            return new Response(ex.Message, new ResponseOptions
            {
                Status = 500,
                StatusText = ex.Message,
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            });
        }
    }

    protected override async Task ServiceWorker_OnMessageAsync(ExtendableMessageEvent e) => Log("ServiceWorker_OnMessageAsync");
    protected override async Task ServiceWorker_OnPushAsync(PushEvent e) => Log("ServiceWorker_OnPushAsync");
}
```

## IDisposable
Some objects implement `IDisposable`, such as `SpawnJSObject` and `Callback` types.

`SpawnJSObject` types dispose their underlying reference when their finalizer is called if not previously disposed.

`Callback` types must be disposed unless created with `Callback.CreateOne`, in which case they dispose themselves after the first callback. Disposing a `Callback` prevents it from being called.

## Unit Testing
This project uses Playwright .Net to enable unit testing in a real web browser with an actual Javascript environment.
- `SpawnDev.SpawnJS.WebWorkers.Demo` - demo project that contains the unit test methods.
- `SpawnDev.SpawnJS.WebWorkers.TestRunner` - the Playwright test runner. It builds and serves the Demo, launches a browser, and reads the `READY` / `TEST` / `RESULTS` console lines.

Run the tests:
```
dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner
dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- SharedWebWorkersByName   # filter by name
dotnet run --project SpawnDev.SpawnJS.WebWorkers.TestRunner -- --headed                 # watch it in a real browser
```

# Support for You
Issues can be reported [here](https://github.com/LostBeard/SpawnDev.SpawnJS.WebWorkers/issues) on GitHub. Create a new [discussion](https://github.com/LostBeard/SpawnDev.SpawnJS.WebWorkers/discussions) to show off your projects and post your ideas.

# Support for Us
Sponsor us via GitHub Sponsors to give us more time to work on SpawnDev.SpawnJS.WebWorkers and other open source projects. Or buy us a cup of coffee via PayPal. All support is greatly appreciated! ♥

[![GitHub Sponsor](https://img.shields.io/github/sponsors/LostBeard?label=Sponsor&logo=GitHub&color=%23fe8e86)](https://github.com/sponsors/LostBeard)
[![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=7QTATH4UGGY9U)

# Thanks
Thank you to everyone who has helped support SpawnDev.SpawnJS and related projects financially, by filing issues, and by improving the code. Every little contribution helps!

SpawnDev.SpawnJS.WebWorkers is the SpawnJS port of SpawnDev.BlazorJS.WebWorkers, which was inspired by Tewr's BlazorWorker implementation. Thank you!
https://github.com/Tewr/BlazorWorker

# The SpawnDev Crew
SpawnDev is built by a small crew:
- **LostBeard** (Todd Tanner) - Captain, library author, keeper of the vision
- **Riker** - First Officer, implementation lead on consuming projects
- **Data** - Operations Officer, deep-library work, test rigor, root-cause analysis
- **Tuvok** - Security/Research Officer, design planning, documentation, code review
- **Geordi** - Chief Engineer, library internals, GPU kernels, backend work
- **Seven** - Wasm backend, GPU kernels, fail-loud verification
