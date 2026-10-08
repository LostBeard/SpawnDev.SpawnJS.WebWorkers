# Blazor WASM support

SpawnDev.SpawnJS.WebWorkers runs the same Blazor WASM app in Dedicated Workers, Shared Workers, and
Service Workers. Each worker is a full separate .Net / Blazor instance (own DI, own runtime). The window
keeps its normal `blazor.webassembly.js` boot; workers use a Blazor-specific entry that sets up a faux
DOM, loads `blazor.webassembly.js`, and calls `Blazor.start()` **before** managed
`WebAssemblyHostBuilder.CreateDefault`.

That order matters: `CreateDefault` calls `NavigationManager` → `Blazor._internal.navigationManager.getBaseURI`,
which requires the `blazor-internal` module registered by `Blazor.start()` via `setModuleImports`. Booting
a worker with plain `dotnet.js` + `runMain` skips that and fails with:

```text
ES6 module blazor-internal was not imported yet, please call JSHost.ImportAsync() first
```

For BlazorJS interop (not SpawnJS), use [SpawnDev.BlazorJS.WebWorkers](https://github.com/LostBeard/SpawnDev.BlazorJS.WebWorkers) instead.

## Packages

```xml
<PackageReference Include="SpawnDev.SpawnJS.Blazor" Version="2.1.*" />
<PackageReference Include="SpawnDev.SpawnJS.WebWorkers" Version="2.1.*" />
```

Your app also references `Microsoft.AspNetCore.Components.WebAssembly` (every Blazor WASM template does); that
package is what ships `blazor.webassembly.js`. The WebWorkers package sets `SpawnJSWebWorkersBlazor=true`
automatically when **both** are true:

- the project uses `Microsoft.NET.Sdk.BlazorWebAssembly` (`UsingMicrosoftNETSdkBlazorWebAssembly=true`), and
- `BlazorWebAssemblyJSPath` is set. `Microsoft.AspNetCore.Components.WebAssembly`'s `build/*.props` sets it, and it is
  the exact property the Blazor SDK adds `blazor.webassembly.js` to your app from.

See [build-properties.md](build-properties.md).

### Blazor SDK without the Blazor JS runtime (SpawnJS.RazorRenderer)

An app can use the Blazor SDK only to compile `.razor` and render through
[SpawnDev.SpawnJS.RazorRenderer](https://github.com/LostBeard/SpawnDev.SpawnJS.RazorRenderer), with no
`Microsoft.AspNetCore.Components.WebAssembly` reference. It has no `blazor.webassembly.js`, so its page boots through
`main.classic.js` / `main.module.js` like a plain .Net WASM app. `BlazorWebAssemblyJSPath` is empty, so
`SpawnJSWebWorkersBlazor` stays `false` and the bundle is built. Nothing to set.

2.1.19 - 2.2.2 checked only the SDK. Those versions put RazorRenderer apps in Blazor mode, so they shipped no
`main.classic.js` and the page failed with `SyntaxError: Unexpected token '<'` (the server's SPA fallback answered
the missing script with `index.html`). The workaround was `<SpawnJSWebWorkersBlazor>false</SpawnJSWebWorkersBlazor>`;
it is now redundant but harmless.

### Why Blazor apps do not get the classic bundle

The bundle is not left off because Rollup fails. It cannot coexist with a Blazor page boot:

1. The bundle requires `WasmBundlerFriendlyBootConfig=true`. That makes the app's own `_framework/dotnet.js`
   import its assets bundler-style (`import dotnet_native from "./dotnet.native.<fp>.wasm"`), which a browser cannot
   load directly. `blazor.webassembly.js` loads `dotnet.js` directly, so the Window would stop booting.
2. `main.classic.js` boots with `dotnet.js` + `runMain`, which skips `Blazor.start()`. `CreateDefault` then throws
   `ES6 module blazor-internal was not imported yet` in the worker (see above).

## Program.cs

```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.WebWorkers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSpawnJSRuntime(out var JS);
builder.Services.AddWebWorkerService();

// Root components only in the Window - workers have no real DOM to host them.
if (JS.IsWindow)
{
    builder.RootComponents.Add<App>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
}

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().SpawnJSRunAsync();
```

## Calling a worker

```csharp
@inject WebWorkerService WebWorkerService

@code {
    async Task RunInWorker()
    {
        using var worker = await WebWorkerService.GetWebWorker();
        await worker!.WhenReady;
        await worker.Run(() => Console.WriteLine("Hello from worker"));
        // Expression calls, GetService<T>(), TaskPool, etc. work the same as plain .Net WASM.
    }
}
```

## index.html (Window)

Keep the stock Blazor bootloader. Do **not** point the page at `main.classic.js` / `main.module.js`
(those are for plain .Net WASM apps).

```html
<script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
```

## What workers load

| App kind | Default worker entry | Boot |
|---|---|---|
| Blazor WASM | `spawndev.spawnjs.webworkers.module.js` (module) | faux-env + parse `index.html` for fingerprinted `blazor.webassembly.*.js` + `Blazor.start()` |
| Blazor WASM (classic script URL) | `spawndev.spawnjs.webworkers.js` | same idea via `importScripts` + import-map-aware `importOverride` |
| Plain .Net WASM | `main.classic.js` | event-holder + `dotnet.js` + `runMain` |
| Blazor SDK, no `blazor.webassembly.js` (RazorRenderer) | `main.classic.js` | same as plain .Net WASM |

Blazor workers default to the **module** entry so native `import()` handles .Net 10 import maps and
`#private` fields. The classic Blazor script remains available if you pass an explicit classic `ScriptUrl`.

## MSBuild notes

- `SpawnJSWebWorkersClassicBundle` defaults to **false** for apps that ship `blazor.webassembly.js` so
  `WasmBundlerFriendlyBootConfig` is not forced onto the page boot (see
  [Why Blazor apps do not get the classic bundle](#why-blazor-apps-do-not-get-the-classic-bundle)).
- `SpawnJSWebWorkersBlazor` stamps `[assembly: SpawnJSWebWorkersBlazor(true)]` so every scope (Window and
  workers) resolves the Blazor entry via reflection - no DOM/fetch.
- With a **ProjectReference** to WebWorkers (Debug inner loop), NuGet does not auto-import `build/`
  props+targets. Import both explicitly, same pattern as the plain Demo:

```xml
<Import Project="..\SpawnDev.SpawnJS.WebWorkers\build\SpawnDev.SpawnJS.WebWorkers.props" Condition="'$(Configuration)'=='Debug'" />
<!-- ... ProjectReference ... -->
<Import Project="..\SpawnDev.SpawnJS.WebWorkers\build\SpawnDev.SpawnJS.WebWorkers.targets" Condition="'$(Configuration)'=='Debug'" />
```

## Demo

`SpawnDev.SpawnJS.WebWorkers.DemoBlazor` in this repo is a minimal Blazor WASM app that creates a worker
and runs `Console.WriteLine` on it.
