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
const isServiceWorkerScope = globalThis.constructor?.name === 'ServiceWorkerGlobalScope';

// In a ServiceWorker, serve the runtime's own files from Cache Storage when the app cached them (an offline PWA):
// a ServiceWorker's fetches never pass through its own fetch handler, so without this a worker that the browser
// restarts while offline cannot load .Net at all - and every request it holds (the page's navigation too) waits.
// JS modules keep the default path (.Net expects a URL for those, not a Response); everything else - assemblies,
// dotnet.native.wasm, ICU data, ... - is matched in the caches first, then fetched.
function cacheFirstResourceLoader(type, name, defaultUri, integrity, behavior) {
    if (type === 'dotnetjs' || String(behavior || '').indexOf('js-module') === 0) return undefined;
    return caches.match(defaultUri, { ignoreSearch: true })
        .then(function (cached) { return cached || fetch(defaultUri, integrity ? { integrity: integrity } : undefined); });
}

async function boot() {
    let builder = dotnet.withApplicationArguments('start');
    if (isServiceWorkerScope && globalThis.caches) builder = builder.withResourceLoader(cacheFirstResourceLoader);
    let runtime;
    try {
        runtime = await builder.create();
    } catch (err) {
        if (isServiceWorkerScope && globalThis.ReleaseMissedServiceWorkerEvents) globalThis.ReleaseMissedServiceWorkerEvents(err);
        throw err;
    }

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
