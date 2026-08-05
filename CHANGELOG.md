# Changelog

All notable changes to SpawnDev.SpawnJS.WebWorkers.

## 1.0.1

- **CDN-correct worker script URLs.** `WebWorkerService` now resolves worker entrypoints against
  `SpawnJSRuntime.AppBaseUri` (the app's own load origin) instead of `document.baseURI` (the host page's
  base). Under a CDN load - where the app is served from a different path than the host page - worker
  scripts (`main.classic.js` / `main.module.js` / `_framework/*`) previously resolved to the page root and
  failed to load; they now resolve to the app origin correctly. Requires `SpawnDev.SpawnJS >= 1.1.4`.

## 1.0.0

First stable release.

- **Classic + module worker bundle.** Workers (Dedicated, Shared, Service) now load from bundled entrypoints
  built from the app's own output - `main.classic.js` (classic, the default) and `main.module.js` (ES module) -
  instead of the previous module-only worker script. The classic bundle loads via a plain `<script src>` or
  `importScripts()`, enabling `<script>`-tag and browser-extension scopes.
- The bundle reuses the app's existing `_framework` output as-is: **only two JS files are added, no assets are
  duplicated.** Produced by an offline, self-contained Rollup toolchain (requires Node.js on PATH at
  build/publish).
- Works for both `dotnet build` (dev / `dotnet run`) and `dotnet publish`, with a skip-when-unchanged cache.
- `WebWorkerService` selects the entrypoint automatically (classic by default, module on request), gated by a
  build-stamped `[assembly: SpawnJSWebWorkersClassicBundle(true)]` attribute (`NonModuleScriptAvailable`).
- The app boots through the bundle (`index.html` -> `main.module.js`); the raw `main.js` is superseded.
  `WasmBundlerFriendlyBootConfig=true` is set automatically (opt out with `SpawnJSWebWorkersClassicBundle=false`).
- **Browser extensions:** publish-only, opt-in `SpawnJSWebWorkersFrameworkFolderName` /
  `SpawnJSWebWorkersContentFolderName` rename the underscore-prefixed `_framework` / `_content` folders (illegal
  at an extension root) and rewrite references in the published output.
- See [Docs/build-properties.md](Docs/build-properties.md) for all MSBuild properties.
