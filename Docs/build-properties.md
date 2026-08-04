# SpawnDev.SpawnJS.WebWorkers - MSBuild properties

These MSBuild properties control how SpawnDev.SpawnJS.WebWorkers builds the worker bundle and shapes the
published output. Set them in a `<PropertyGroup>` in your app's `.csproj`. See the
[README](../README.md#worker-bundle) for the conceptual overview.

| Property | Default | When it applies | Purpose |
|---|---|---|---|
| `SpawnJSWebWorkersClassicBundle` | `true` | build + publish | Master opt-out. When `true`, the package builds the two bundled worker entrypoints (`main.classic.js`, `main.module.js`) from your app's own output and sets `WasmBundlerFriendlyBootConfig=true`. Set `false` to skip the bundle entirely (the app is then a normal .Net WASM app and worker creation falls back to the legacy `spawndev.spawnjs.webworkers.module.js`, which only works with asset fingerprinting off). |
| `WasmBundlerFriendlyBootConfig` | set to `true` by this package when the bundle is enabled | build + publish | .Net SDK property. Makes the app's `dotnet.js` use static (bundler-followable) imports so the bundle can be produced from - and reference - the app's own `_framework`. A consequence: the raw `main.js` is not directly browser-runnable, so your app boots through the bundle (`main.module.js`). You normally do not set this yourself. |
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
