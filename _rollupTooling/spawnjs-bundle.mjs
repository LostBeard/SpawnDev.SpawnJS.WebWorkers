// spawnjs-bundle.mjs  (SOURCE - dev-only)
// -----------------------------------------------------------------------------
// Programmatic, self-contained SpawnJS.WebWorkers app bundler. Uses the pure-WASM
// Rollup build (@rollup/wasm-node) + plugins so the whole thing can be esbuild-bundled
// into ONE file (buildcontent/rollup.bundled.mjs) that ships in the NuGet package and
// runs with only `node` on PATH - no npm, no node_modules, offline.
//
// Usage (run by the MSBuild task at consumer build/publish):
//   node rollup.bundled.mjs <loaderEntry.js> <outDir> [<appWwwroot>]
//
// Produces ONLY two BUNDLED JS entrypoints in <outDir> from a WasmBundlerFriendlyBootConfig
// output's dotnet.js graph (both run the event-holder first, then boot .Net):
//   main.module.js   (ES module - <script type=module> / import)
//   main.classic.js  (UMD/IIFE  - <script src> in window AND importScripts() in a worker)
//
// The bundle does NOT emit or copy the app's .wasm/.dat assets - it REUSES the existing
// _framework output as-is (see frameworkAssetsPlugin below). So exactly two files are added
// to the output. This requires the bundle to be built from the SAME output it will be served
// beside (build vs publish fingerprint the assemblies differently), so the asset names the
// bundle references match the _framework files actually present.
//
// <appWwwroot> is the app output root the bundled modules were read from - the folder the two
// entrypoints are served from. It is what lets originBasePlugin keep each bundled module's
// ORIGINAL base URL; see that plugin for why that matters.

import { rollup } from '@rollup/wasm-node';
import nodeResolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';
import { join, relative, resolve as resolvePath } from 'node:path';

const [input, outDir, wwwrootArg] = process.argv.slice(2);
if (!input || !outDir) {
    console.error('usage: node rollup.bundled.mjs <loaderEntry.js> <outDir> [<appWwwroot>]');
    process.exit(2);
}
const wwwroot = wwwrootArg ? resolvePath(wwwrootArg) : null;

// The URL the bundle itself was loaded from, captured ONCE at top-level evaluation (the
// `intro` below) and referenced by name everywhere else:
//   - ES module: import.meta.url, native and always correct.
//   - classic (iife/umd/cjs): import.meta.url does not exist, so resolve the script's own URL
//     from document.currentScript in a window and self.location in a worker. currentScript is
//     only set DURING synchronous top-level evaluation, which is exactly when the intro runs;
//     reading it later (from an async continuation) yields null and silently falls back to the
//     PAGE url - wrong whenever the app is served from a different path than the page (CDN
//     load). Capturing once at eval time is both correct and far smaller than inlining the
//     expression at each of its several hundred uses.
const SELF = '__spawnjsBundleUrl';
const INTRO_ES = `const ${SELF} = import.meta.url;\n`;
const INTRO_CLASSIC =
    `var ${SELF} = (typeof document !== 'undefined' && document.currentScript ? document.currentScript.src`
    + ` : typeof self !== 'undefined' && self.location ? self.location.href`
    + ` : typeof location !== 'undefined' ? location.href : '');\n`;

// Reuse the existing _framework output instead of emitting/copying assets.
// In a WasmBundlerFriendlyBootConfig dotnet.js graph every asset is a STATIC import:
//   import X_wasm from "./System.Private.CoreLib.8gxeyou5i5.wasm"   (assemblies + dotnet.native.wasm)
//   import Y_dat  from "./dotnet.timezones.blat"  / ICU ".dat"
// The default export of such an asset module is its URL. We resolve each to a tiny virtual
// module whose default export is the URL of the SAME-named file under _framework/, resolved at
// runtime against the bundle's own location. Nothing is emitted or renamed, so the bundle
// references the real _framework assets that are already there.
// Every non-JS runtime asset a WasmBundlerFriendlyBootConfig dotnet.js statically imports:
//   .wasm (assemblies + dotnet.native), .dat/.blat (ICU + timezone data), .pdb (debug symbols, Debug builds).
// These stay EXTERNAL and resolve to the existing _framework output; only the JS graph is bundled.
const assetRe = /\.(wasm|dat|blat|pdb)$/i;
const FW_PREFIX = '\0spawnjs-fw-asset:';
const frameworkAssetsPlugin = {
    name: 'spawnjs-framework-assets',
    resolveId(source) {
        if (assetRe.test(source)) {
            // Keep only the file name (the bundler-friendly import path is like "./Name.<fp>.wasm");
            // that name matches the file in _framework of the output this bundle is built from.
            const name = source.substring(source.lastIndexOf('/') + 1);
            return FW_PREFIX + name;
        }
        return null;
    },
    load(id) {
        if (id.startsWith(FW_PREFIX)) {
            const name = id.slice(FW_PREFIX.length);
            // Default export = URL string of the real _framework asset, relative to the bundle.
            return `export default new URL(${JSON.stringify('_framework/' + name)}, ${SELF}).href;`;
        }
        return null;
    },
};

/**
 * Path of a bundled module inside the app output, relative to the app root, or null when the
 * module is not part of that output - the staged loader/event-holder and the virtual asset
 * modules above, both of which ARE the bundle and so are already app-root relative.
 */
function appPath(moduleId) {
    if (!wwwroot || !moduleId || moduleId.startsWith('\0')) return null;
    let rel;
    try { rel = relative(wwwroot, resolvePath(moduleId)); } catch { return null; }
    if (!rel || rel.startsWith('..')) return null;
    return rel.split(/[\\/]/).join('/');
}

// Keep every bundled module's ORIGINAL base URL.
// -----------------------------------------------------------------------------
// Bundling lifts the whole boot graph out of _framework/ and up to the app root, which
// silently changes the base URL of everything the .Net runtime resolves AT RUNTIME:
//   * `import.meta.url` - the loader stores it as loaderHelpers.scriptUrl and derives
//     scriptDirectory (and locateFile) from it. Left alone, scriptDirectory becomes the app
//     root instead of _framework/.
//   * a dynamic `import(url)` whose specifier is NOT a literal - Rollup cannot rewrite it,
//     because the string only exists at runtime. That is how EVERY JSHost.ImportAsync module
//     is loaded: dotnet.runtime.js does a bare `import(module_url)`. So Avalonia's
//     JSHost.ImportAsync("avalonia", "./avalonia.js") resolved to _framework/avalonia.js
//     unbundled, and to /avalonia.js (404) from a root-level bundle. The same applies to any
//     library or app that ships a JS module beside _framework and imports it relatively.
// So: report each module's own original location, and resolve its relative dynamic imports
// against its own original directory. The bundle then resolves exactly as the unbundled
// output did - no import map, and nothing copied to the app root.
const originBasePlugin = {
    name: 'spawnjs-origin-base',
    resolveImportMeta(property, { moduleId }) {
        if (property !== 'url') return null;
        const rel = appPath(moduleId);
        // Not part of the app output -> it really is the bundle's own URL.
        if (rel === null) return SELF;
        // The module's original URL, carrying over the bundle URL's ?query#hash - the .Net
        // loader keeps that as modulesUniqueQuery and appends it to resources it fetches.
        return `new URL(${JSON.stringify(rel)} + String(${SELF}).replace(/^[^?#]*/, ''), ${SELF}).href`;
    },
    renderDynamicImport({ moduleId, targetModuleId }) {
        // Resolved into the bundle by Rollup - it renders those itself.
        if (targetModuleId !== null) return null;
        const rel = appPath(moduleId);
        if (rel === null) return null;
        const slash = rel.lastIndexOf('/');
        if (slash < 0) return null;   // already at the app root - same base the bundle has
        const base = `new URL(${JSON.stringify(rel.slice(0, slash + 1))}, ${SELF}).href`;
        // Only "./x" and "../x" are re-rooted. Bare specifiers ("process", "module"), absolute
        // URLs and root-relative paths mean the same thing from either location - pass through.
        return {
            left: `import(((s) => typeof s === "string" && s[0] === "." && (s[1] === "/" || (s[1] === "." && s[2] === "/"))`
                + ` ? new URL(s, ${base}).href : s)(`,
            right: '))',
        };
    },
};

function onwarn(warning, warn) {
    // dotnet.native.*.js is large emscripten glue; silence expected noise.
    if (warning.code === 'EVAL' || warning.code === 'THIS_IS_UNDEFINED' || warning.code === 'CIRCULAR_DEPENDENCY') return;
    warn(warning);
}

const bundle = await rollup({
    input,
    plugins: [
        // Keeps import.meta.url + runtime dynamic imports pointing at each module's origin.
        originBasePlugin,
        // Reference the existing _framework .wasm/.dat as-is (no emit, original names).
        frameworkAssetsPlugin,
        nodeResolve({ extensions: ['.js'] }),
        commonjs(),
    ],
    onwarn,
});

await bundle.write({ file: join(outDir, 'main.module.js'), format: 'es', intro: INTRO_ES });
await bundle.write({ file: join(outDir, 'main.classic.js'), format: 'iife', intro: INTRO_CLASSIC });
await bundle.close();

console.log(`SpawnJS bundle: wrote main.module.js + main.classic.js to ${outDir}`);
