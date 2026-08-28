# Changelog

All notable changes to SpawnDev.SpawnJS.WebWorkers.

## 2.1.10

- **Requires `SpawnDev.SpawnJS` >= 2.1.9**, which is where the app-root resolver stopped matching the
  framework folder BY NAME. That is the other half of the `SpawnJSWebWorkersFrameworkFolderName` fix
  released in 2.1.9: this package rewrites the backslash-escaped reference in the published output, and
  SpawnJS no longer depends on the name at all. Either alone is sufficient - both were verified
  independently - but a WebWorkers consumer only ever receives SpawnJS transitively, so without this
  version bump the SpawnJS fix could not reach one.

  SpawnJS 2.1.9 identifies the framework folder by WHAT was loaded rather than what the folder is called:
  the runtime entry and every boot-manifest resource live in it, so the app root is its parent, while a
  bundled `main.classic.js` / `main.module.js` already sits AT the app root. No behaviour change for a
  normal `_framework` publish.

## 2.1.9

- **The bundle now keeps every bundled module's ORIGINAL base URL** (GitHub issue #1). Bundling lifts the
  whole boot graph out of `_framework/` up to the app root, which silently re-based everything the .Net
  runtime resolves at RUNTIME - and Rollup cannot rewrite those, because the URLs only exist at runtime:
  - `import.meta.url`, which the loader stores as `loaderHelpers.scriptUrl` and turns into
    `scriptDirectory` / `locateFile`. It reported the app root instead of `_framework/`.
  - a dynamic `import(url)` with a non-literal specifier - which is how EVERY `JSHost.ImportAsync` module
    is loaded (`dotnet.runtime.js` does a bare `import(module_url)`). Avalonia's
    `JSHost.ImportAsync("avalonia", "./avalonia.js")` therefore resolved to `_framework/avalonia.js`
    unbundled and to `/avalonia.js` - a 404 - from the bundle, and the same applied to any library or app
    that ships a JS module beside `_framework` and imports it relatively.

  The bundler now reports each module's own original location and resolves its relative dynamic imports
  against its own original directory, so the bundle resolves exactly as the unbundled output did. Avalonia
  browser apps work with no import map and no copy of `avalonia.js` into wwwroot. The classic bundle also
  captures its own script URL ONCE at top-level evaluation instead of re-reading `document.currentScript`
  at each use (which is null from an async continuation and silently fell back to the PAGE url under a CDN
  load) - which also makes `main.classic.js` smaller.
- **`SpawnJSWebWorkersFrameworkFolderName` now also rewrites backslash-escaped references** to the renamed
  folder (`_framework\/`, the form a JS regex literal or a JSON string uses). SpawnJS derives the app root
  with `path.replace(/(^|\/)_framework\/$/, '$1')`; with that reference missed, the app root pointed
  INSIDE the renamed folder and every worker was requested at `<root>/framework/main.classic.js` (404).
- **A needed static web asset that has no file on disk is now reported.** The build path silently skipped
  any `_framework/*` or `*.lib.module.js` asset whose candidate paths did not exist, which surfaced much
  later as an unresolved import naming the importer instead of the missing asset.
- **Fingerprint placeholders that state their value are honored.** `name#[.{fingerprint=abc}]!.ext` means
  the fingerprint IS `abc`; the task substituted the item's own `Fingerprint` metadata instead, which is a
  different value whenever the placeholder names another asset's fingerprint. The unresolved-import error
  also now lists what was staged under a near-miss name, so a fingerprint mismatch is visible rather than
  guessed at.

## 2.1.3

- **Bundle no longer stages a stale build-output copy of an asset.** A static web asset has two paths:
  `Identity` (its logical location, which for a generated asset is a COPY in the build output) and
  `OriginalItemSpec` (the producer it was copied from, typically under `obj/`). The bundle task preferred
  the copy whenever it existed - but MSBuild does not always refresh it. Upgrading a package that ships a
  `*.lib.module.js` JS initializer changes that initializer's fingerprint and the SDK regenerates the
  bundler-friendly `obj/dotnet.js`, while `bin/<cfg>/<tfm>/wwwroot/_framework/dotnet.js` is left behind
  still statically importing the OLD fingerprinted name. Fingerprints are fixed width, so the stale copy is
  the SAME LENGTH and no size check notices. The build then failed with a bare Rollup
  `Could not resolve ./../<name>.<oldfp>.lib.module.js` - and the same stale copy is what a dev server would
  serve, under a route and integrity computed from the fresh content, so the app was broken bundled or not.
  The task now picks the candidate whose SHA-256 matches the asset's own `Integrity` (which is computed from
  the producer), reports when it had to fall back to the producer, and warns if neither candidate matches.
- **Unresolved boot-graph imports are reported as a build error, not a Rollup stack trace.** Before running
  Rollup the task verifies that every relative static import in the assembled `dotnet(.<fp>).js` resolves to
  a file this build actually produced, and names the importer and each missing target if not.

## 1.0.6

- **Classic/module bundle now only runs for applications, not class libraries.** The bundle enable gate
  additionally requires `OutputType == Exe`. A WASM/Blazor app (which has an entrypoint and a `_framework`
  runtime) is `Exe`; a Razor class library is `Library` and has neither. Previously an RCL that merely
  referenced this package would attempt the rollup during its own build and fail with
  `_framework not found under ...spawnjs-bundle\build\stage\wwwroot`. RCLs now skip the bundle automatically -
  no per-library `<SpawnJSWebWorkersClassicBundle>false</SpawnJSWebWorkersClassicBundle>` opt-out required
  (the flag still works as an explicit override). No effect on WASM/Blazor app builds.

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
