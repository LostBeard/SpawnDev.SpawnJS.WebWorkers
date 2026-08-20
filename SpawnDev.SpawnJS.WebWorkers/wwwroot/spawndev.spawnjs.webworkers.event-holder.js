// Holds events that happen while Blazor WASM is loading

(function () {
    // Optional verbose logger. Silent unless globalThis.spawnjsVerbose is truthy.
    // (Previously this file referenced an undefined `consoleLog`, which threw a
    // ReferenceError the moment an event was actually missed - breaking the hold.)
    function consoleLog() {
        if (!globalThis.spawnjsVerbose) return;
        console.log.apply(console, arguments);
    }
    var isBackgroundExtensionScript = globalThis.location?.href?.indexOf('-extension://') !== -1;
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
    } else if (globalThisTypeName == 'ServiceWorkerGlobalScope' && !isBackgroundExtensionScript) {
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
    }
})()