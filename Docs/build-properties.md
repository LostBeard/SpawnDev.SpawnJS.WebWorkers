# SpawnDev.SpawnJS.WebWorkers - MSBuild properties

These MSBuild properties control how SpawnDev.SpawnJS.WebWorkers builds the worker bundle and shapes the
published output. Set them in a `<PropertyGroup>` in your app's `.csproj`. See the
[README](../README.md#worker-bundle) for the conceptual overview.

They apply whether your app references this package directly or only through another package or project (2.2.4+,
via `buildTransitive/`). Only browser WASM applications are affected (`Microsoft.NET.Sdk.WebAssembly` or the Blazor
WASM SDK, and `OutputType=Exe`); a server, test project, or class library that reaches this package through a
reference is left alone. See [How the boot path is chosen](../README.md#how-the-boot-path-is-chosen).

| Property | Default | When it applies | Purpose |
|---|---|---|---|
| `SpawnJSWebWorkersBlazor` | `true` when the app ships `blazor.webassembly.js`: `UsingMicrosoftNETSdkBlazorWebAssembly` is `true` **and** `BlazorWebAssemblyJSPath` is set (by `Microsoft.AspNetCore.Components.WebAssembly`); else `false`. A Blazor SDK app without that package (SpawnJS.RazorRenderer) is `false`. | build + publish | Enables Blazor worker boot. Stamps `[assembly: SpawnJSWebWorkersBlazor(true)]` so `WebWorkerService` loads `spawndev.spawnjs.webworkers.module.js` by default (faux-env + fingerprinted `blazor.webassembly.js` + `Blazor.start`) instead of the plain .Net WASM bundle. Also defaults `SpawnJSWebWorkersClassicBundle` to `false` so `WasmBundlerFriendlyBootConfig` is not forced onto a stock Blazor page boot. See [blazor.md](blazor.md). |
| `SpawnJSWebWorkersClassicBundle` | `false` when `SpawnJSWebWorkersBlazor` is `true` (the app ships `blazor.webassembly.js`); else `true` (plain .Net WASM, and Blazor SDK apps without the Blazor JS runtime such as SpawnJS.RazorRenderer) | build + publish | Master opt-out for the classic/module bundle. When `true`, builds `main.classic.js` / `main.module.js` and sets `WasmBundlerFriendlyBootConfig=true`. When `false` on a plain .Net WASM app, worker creation falls back to `spawndev.spawnjs.webworkers.dotnet.module.js` (fingerprinting must be off). |
| `WasmBundlerFriendlyBootConfig` | set to `true` by this package when the bundle is enabled | build + publish | .Net SDK property. Makes the app's `dotnet.js` use static (bundler-followable) imports so the bundle can be produced from - and reference - the app's own `_framework`. A consequence: the raw `main.js` is not directly browser-runnable, so your app boots through the bundle (`main.module.js`). You normally do not set this yourself. Not forced for apps that ship `blazor.webassembly.js` (bundle defaults off there). |
| `SpawnJSWebWorkersFrameworkFolderName` | empty (no rename) | **publish only** | Renames the published `wwwroot/_framework` folder to this name and rewrites every reference to it in the published `.js`/`.mjs`/`.html`/`.json`/`.css`/`.webmanifest`/`.map` (including `main.classic.js`, `main.module.js`, `index.html`, and the boot config). For running where leading-underscore paths are illegal, e.g. browser extensions. Must not start with `_` or `.` and must be a single folder name. |
| `SpawnJSWebWorkersContentFolderName` | empty (no rename) | **publish only** | Same as above but for the `wwwroot/_content` folder (Razor Class Library static assets). Only needed if your app has a `_content` folder. Same constraints. |

## Requirements

- **Node.js on PATH** at build and publish - the bundle is produced by an offline, self-contained Rollup
  toolchain that runs under Node (no `npm install`, no network access).

## Entrypoints produced

| File | Kind | Notes |
|---|---|---|
| `main.js` | app default (untouched) | Not used once the app is bundler-friendly; safe to delete when `index.html` boots via the bundle. |
| `main.classic.js` | classic (non-module) | Default for new Worker/SharedWorker/ServiceWorker; loadable via `<script src>` or `importScripts()`. |
| `main.module.js` | ES module | Recommended page entrypoint (`<script type="module" src="main.module.js">`), and used when a module worker is requested. |

Both bundled entrypoints reference your app's existing `_framework` output as-is - no assets are duplicated,
only the two JS files are added.

## Browser extensions

Because `main.classic.js` loads via a plain `<script>` / `importScripts()` and reuses the app's own
`_framework`, it can run in a browser extension background ServiceWorker and content scripts. Extensions
forbid root files/folders that start with `_`, so use `SpawnJSWebWorkersFrameworkFolderName` (and
`SpawnJSWebWorkersContentFolderName` if you have RCL static assets) to remove the underscore-prefixed folders
on publish.

Caveats:
- These rename properties are **publish-only** - normal builds and `dotnet run` keep the default folder names.
- The rewrite only touches references it can see in published text files. A path built at runtime in C#
  (e.g. an RCL that constructs `"_content/…"` in code, baked into the `.wasm`) is **not** rewritten and will
  break. Only opt in if you understand your app's (and your dependencies') asset loading. RCLs that hardcode
  the underscore path are not supported by the rename.
- An alternative that avoids the rename entirely is to place the whole app under a subfolder (e.g. `app/`) with
  the extension `manifest.json` at the extension root; the underscore-prefixed folders are then not at the root.
