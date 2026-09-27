// SpawnDev.SpawnJS.WebWorkers - Blazor WASM module-worker entrypoint
// -----------------------------------------------------------------------------
// Boots Blazor in a DedicatedWorker / SharedWorker via faux-env + Blazor.start so
// setModuleImports("blazor-internal", ...) runs BEFORE managed CreateDefault
// (which calls NavigationManager getBaseURI). Fingerprinted blazor.webassembly.*.js
// is resolved from index.html / import maps - never hard-coded unfingerprinted.
//
// Classic (non-module) workers use spawndev.spawnjs.webworkers.js instead, which
// importScripts the same faux-env and injects scripts from index.html.

import './spawndev.spawnjs.webworkers.event-holder.js';
import './spawndev.spawnjs.webworkers.faux-env.js';

const params = new Proxy(new URLSearchParams(globalThis.location.search), {
    get: (searchParams, prop) => searchParams.get(prop),
});
if (globalThis.constructor?.name !== 'Window') {
    globalThis.autoStart ??= params.autoStart !== '0';
}

var verboseWebWorkers = !!params.verbose;
globalThis.consoleLog ??= function () {
    if (!verboseWebWorkers) return;
    console.log(...arguments);
};

// do NOT call document.initDocument() here - bootBlazor builds the faux DOM first, then inits once

var disableHotReload = true;
if (disableHotReload) {
    self._dotnet_watch_ws_injected = true;
}

var documentBaseURIIsModified = false;
var documentBaseURI = (function () {
    var uri = new URL('./', location.href);
    if (uri.pathname.includes('_content/')) {
        documentBaseURIIsModified = true;
        var subpath = uri.pathname.substring(0, uri.pathname.indexOf('_content/'));
        return new URL(subpath, location.href).toString();
    }
    return uri.toString();
})();
document.baseURI = documentBaseURI;

function getAppURL(relativePath) {
    var ret = new URL(relativePath, documentBaseURI).toString();
    if (self.indexImportMaps) {
        for (var mapSet of self.indexImportMaps) {
            if (mapSet.imports[ret]) {
                return mapSet.imports[ret];
            }
        }
    }
    return ret;
}

if (documentBaseURIIsModified) {
    let fetchOrig = self.fetch;
    self.fetch = function (resource, options) {
        if (typeof resource === 'string') {
            return fetchOrig(getAppURL(resource), options);
        }
        return fetchOrig(resource, options);
    };
}

async function getText(href) {
    var response = await fetch(getAppURL(href));
    return await response.text();
}

function getScriptNodes(indexHtmlSrc) {
    var scriptNodes = [];
    var scriptPatt = /<script\s*(.*?)(?:\s*\/>|\s*>(.*?)<\/script>)/gms;
    var attributesPatt = /([^\s=]+)(?:=(?:"([^"]*)"|'([^"]*)'|([^\s=]+)))?/gm;
    var m = scriptPatt.exec(indexHtmlSrc);
    while (m) {
        let scriptNode = {
            attributes: {},
            text: m[2],
        };
        let scriptTagAttributes = m[1];
        let attrMatch = attributesPatt.exec(scriptTagAttributes);
        while (attrMatch) {
            let attrName = attrMatch[1];
            let attrValue = '';
            if (attrMatch[2]) attrValue = attrMatch[2];
            else if (attrMatch[3]) attrValue = attrMatch[3];
            else if (attrMatch[4]) attrValue = attrMatch[4];
            scriptNode.attributes[attrName] = attrValue;
            attrMatch = attributesPatt.exec(scriptTagAttributes);
        }
        scriptNodes.push(scriptNode);
        m = scriptPatt.exec(indexHtmlSrc);
    }
    return scriptNodes;
}

function resolveBlazorScriptSrc(src) {
    // Prefer import-map / getAppURL resolution (handles fingerprint placeholders
    // already expanded in the served index.html, and importmap aliases).
    return getAppURL(src);
}

async function bootBlazor() {
    var indexHtml = params.indexHtml ?? './';
    if (typeof indexHtml === 'string' && ['true', '1'].indexOf(indexHtml.toLowerCase()) !== -1) {
        indexHtml = 'index.html';
    }
    var browserExtension = (self.browser && self.browser.runtime && self.browser.runtime.id)
        || (self.chrome && self.chrome.runtime && self.chrome.runtime.id)
        || location.href.indexOf('chrome-extension') === 0;
    if (browserExtension) indexHtml = 'index.html';

    var indexHtmlSrc = await getText(indexHtml);
    var scriptNodes = getScriptNodes(indexHtmlSrc);

    self.indexImportMaps = [];
    for (var scriptNode of scriptNodes) {
        if (scriptNode.attributes['type'] == 'importmap') {
            try {
                let importMap = JSON.parse(scriptNode.text);
                let resolvedMaps = {};
                for (let k in importMap.imports) {
                    let v = importMap.imports[k];
                    let kUrl = new URL(k, documentBaseURI).toString();
                    var vUrl = new URL(v, documentBaseURI).toString();
                    resolvedMaps[kUrl] = vUrl;
                }
                Object.assign(importMap.imports, resolvedMaps);
                self.indexImportMaps.push(importMap);
            } catch (ex) {
                consoleLog('error parsing the importmap', ex);
            }
        }
    }

    var blazorSrc = null;
    for (var node of scriptNodes) {
        let src = node.attributes.src;
        if (!src) continue;
        // Prefer standalone WASM runtime; map united -> wasm when needed.
        if (src.includes('_framework/blazor.web.')) {
            src = src.replace('_framework/blazor.web.', '_framework/blazor.webassembly.');
        }
        if (src.includes('_framework/blazor.webassembly.')) {
            blazorSrc = resolveBlazorScriptSrc(src);
            break;
        }
    }
    if (!blazorSrc) {
        throw new Error('SpawnJS.WebWorkers: could not find _framework/blazor.webassembly.* in index.html');
    }

    // Minimal faux DOM nodes Blazor expects.
    var htmlEl = document.appendChild(document.createElement('html'));
    var headEl = htmlEl.appendChild(document.createElement('head'));
    var bodyEl = htmlEl.appendChild(document.createElement('body'));
    var appDiv = bodyEl.appendChild(document.createElement('div'));
    appDiv.setAttribute('id', 'app');
    var errorDiv = bodyEl.appendChild(document.createElement('div'));
    errorDiv.setAttribute('id', 'blazor-error-ui');
    if (document.initDocument) document.initDocument();

    consoleLog('spawndev.spawnjs.webworkers.module: importing', blazorSrc);
    await import(blazorSrc);

    if (globalThis.autoStart) {
        if (typeof Blazor === 'undefined' || typeof Blazor.start !== 'function') {
            throw new Error('SpawnJS.WebWorkers: Blazor.start not available after loading ' + blazorSrc);
        }
        Blazor._startTask = Blazor.start();
    }
}

await bootBlazor();
