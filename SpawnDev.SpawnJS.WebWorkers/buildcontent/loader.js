// SpawnDev.SpawnJS.WebWorkers - classic/module bundle loader (Rollup ENTRY)
// -----------------------------------------------------------------------------
// The MSBuild bundle task copies this file into the bundler-friendly publish's
// wwwroot (next to spawndev.spawnjs.webworkers.event-holder.js and _framework/)
// and Rollup bundles it into main.module.js (es) + main.classic.js (umd).
//
// The event-holder import is FIRST (side-effect) and load-bearing: it registers
// SharedWorker/ServiceWorker event listeners at top-level sync eval so early
// events (onconnect, install, fetch, ...) are captured and held while the async
// .Net runtime boots below. It must evaluate before the dotnet module graph.
import './spawndev.spawnjs.webworkers.event-holder.js';

import { dotnet } from './_framework/dotnet.js';

// Asset URLs (assemblies, dotnet.native.wasm, ICU .dat) are supplied by the bundle's
// frameworkAssetsPlugin as `new URL('_framework/<name>', import.meta.url)` - i.e. resolved
// against THIS bundle's own location, pointing at the existing _framework output that ships
// beside it. Rollup rewrites import.meta.url per format (native in the es bundle; a
// document.currentScript / self.location shim in the classic bundle), so it self-resolves:
//   - <script src=".../main.classic.js">  -> .../_framework/<name>
//   - new Worker(".../main.classic.js")    -> the worker script folder's _framework/<name>
// No withResourceLoader re-rooting is needed: the bundle reuses the real _framework assets
// as-is (nothing is emitted or renamed), so the default URIs are already correct.
async function boot() {
    const runtime = await dotnet
        .withApplicationArguments('start')
        .create();

    // Dispatch the managed entry point (Program.cs). It runs until Exit(), so
    // runMain() may never resolve - do NOT await it for readiness, just surface a
    // startup error if one is thrown.
    Promise.resolve().then(() => runtime.runMain()).catch(err => console.error('SpawnJS runMain error:', err));
    return runtime;
}

// Auto-boot on include so a plain <script> / importScripts / import "just works".
// Nothing is exposed on globalThis by the loader: each bundle is its own closure,
// so multiple SpawnJS apps on one page/worker stay isolated. (The event-holder
// intentionally uses globalThis in ServiceWorker/SharedWorker scopes only, where a
// scope is single-app by nature - that is the fixed .Net drain contract.)
//
// No ad-hoc ServiceWorker install/waitUntil keep-alive here: in a ServiceWorker the
// event-holder captured `install` at top-level sync eval and holds it via
// e.waitUntil(promise), keeping the SW alive through the entire async boot until the
// .Net side's ServiceWorkerEventHandler drains and resolves it.
boot();
