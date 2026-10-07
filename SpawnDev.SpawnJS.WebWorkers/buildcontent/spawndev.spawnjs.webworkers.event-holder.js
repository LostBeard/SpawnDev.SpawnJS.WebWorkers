// Holds events that happen while Blazor WASM is loading

(function () {
    // Optional verbose logger. Silent unless globalThis.spawnjsVerbose is truthy.
    // (Previously this file referenced an undefined `consoleLog`, which threw a
    // ReferenceError the moment an event was actually missed - breaking the hold.)
    function consoleLog() {
        if (!globalThis.spawnjsVerbose) return;
        console.log.apply(console, arguments);
    }
    var globalThisTypeName = globalThis.constructor?.name;
    if (globalThisTypeName == 'SharedWorkerGlobalScope') {
        // important for SharedWorker
        // catch any incoming connections that happen while .Net is loading
        let _missedConnections = [];
        globalThis.takeOverOnConnectEvent = function (newConnectFunction) {
            var tmp = _missedConnections;
            _missedConnections = [];
            globalThis.onconnect = newConnectFunction;
            return tmp;
        }
        globalThis.onconnect = function (e) {
            _missedConnections.push(e.ports[0]);
        };
    } else if (globalThisTypeName == 'ServiceWorkerGlobalScope') {
        // Blazor's service worker asset manifest (ServiceWorkerConfig.ImportServiceWorkerAssets adds the
        // importServiceWorkerAssets query parameter to the worker URL). The legacy worker script imported it; the
        // classic/module bundle did not, so ServiceWorkerEventHandler.AssetsManifest was always null for bundle apps.
        // importScripts is only allowed in a classic worker and only during this first synchronous evaluation.
        try {
            var assetsParam = new URL(globalThis.location.href).searchParams.get('importServiceWorkerAssets');
            if (assetsParam) {
                var manifestUrl = assetsParam.indexOf('.js') !== -1 ? assetsParam : 'service-worker-assets.js';
                importScripts(new URL(manifestUrl, globalThis.location.href).href);
            }
        } catch (err) {
            console.error('SpawnJS.WebWorkers: could not import the service worker asset manifest (a module service worker cannot importScripts; use a classic one):', err);
        }
        var isExtensionScope = globalThis.location?.href && globalThis.location.href.indexOf('-extension://') !== -1;
        if (!isExtensionScope) {
            // .Net Wasm startup is async. This holds the synchronously fired ewvents for so they are available when .Net Wasm starts
            // !isBackgroundExtensionScript is used to prevent this from attaching events when i na browser extension service worker because it will
            // cause script suspend/resume to fail withotu ANY error.
            let holdEvents = true;
            let missedServiceWorkerEvents = [];
            function handleMissedEvent(e) {
                if (!holdEvents) return;
                consoleLog('ServiceWorker missed event:', e.type, e);
                if (e.respondWith) {
                    // fetch and canmakepayment ExtendableEvents use respondWith
                    var responsePromise = new Promise(function (resolve, reject) {
                        e.responseResolve = resolve;
                        e.responseReject = reject;
                    });
                    e.respondWith(responsePromise);
                } else if (e.waitUntil) {
                    // all other ExtendableEvents use waitUntil
                    var waitUntilPromise = new Promise(function (resolve, reject) {
                        e.waitResolve = resolve;
                        e.waitReject = reject;
                    });
                    e.waitUntil(waitUntilPromise);
                }
                missedServiceWorkerEvents.push(e);
            }
            globalThis.addEventListener('activate', handleMissedEvent);
            globalThis.addEventListener('backgroundfetchabort', handleMissedEvent);
            globalThis.addEventListener('backgroundfetchclick', handleMissedEvent);
            globalThis.addEventListener('backgroundfetchfail', handleMissedEvent);
            globalThis.addEventListener('backgroundfetchsuccess', handleMissedEvent);
            globalThis.addEventListener('canmakepayment', handleMissedEvent);
            globalThis.addEventListener('contentdelete', handleMissedEvent);
            globalThis.addEventListener('cookiechange', handleMissedEvent);
            globalThis.addEventListener('fetch', handleMissedEvent);
            globalThis.addEventListener('install', handleMissedEvent);
            globalThis.addEventListener('message', handleMissedEvent);
            globalThis.addEventListener('messageerror', handleMissedEvent);
            globalThis.addEventListener('notificationclick', handleMissedEvent);
            globalThis.addEventListener('notificationclose', handleMissedEvent);
            globalThis.addEventListener('paymentrequest', handleMissedEvent);
            globalThis.addEventListener('periodicsync', handleMissedEvent);
            globalThis.addEventListener('push', handleMissedEvent);
            globalThis.addEventListener('pushsubscriptionchange', handleMissedEvent);
            globalThis.addEventListener('sync', handleMissedEvent);
            // This method will be called by Blazor WASM when it starts up to collect missed events and handle them
            globalThis.GetMissedServiceWorkerEvents = function () {
                holdEvents = false;
                var ret = missedServiceWorkerEvents;
                missedServiceWorkerEvents = [];
                return ret;
            };
            // Called by the bundle loader when .Net cannot start in this worker (e.g. offline with nothing cached):
            // without it, every held fetch - including the page's own navigation - would wait forever. Fetches go to
            // the network (and fail normally when offline); other events complete.
            globalThis.ReleaseMissedServiceWorkerEvents = function (reason) {
                holdEvents = false;
                var held = missedServiceWorkerEvents;
                missedServiceWorkerEvents = [];
                console.error('SpawnJS.WebWorkers: .Net did not start in the service worker; releasing ' + held.length + ' held event(s):', reason);
                for (var i = 0; i < held.length; i++) {
                    var e = held[i];
                    try {
                        if (e.responseResolve) e.responseResolve(e.type === 'fetch' ? fetch(e.request) : undefined);
                        else if (e.waitResolve) e.waitResolve();
                    } catch (err) { /* the event is already settled */ }
                }
            };
        }
    }
})()