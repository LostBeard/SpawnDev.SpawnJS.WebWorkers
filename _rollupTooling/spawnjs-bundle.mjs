// spawnjs-bundle.mjs  (SOURCE - dev-only)
// -----------------------------------------------------------------------------
// Programmatic, self-contained SpawnJS.WebWorkers app bundler. Uses the pure-WASM
// Rollup build (@rollup/wasm-node) + plugins so the whole thing can be esbuild-bundled
// into ONE file (buildcontent/rollup.bundled.mjs) that ships in the NuGet package and
// runs with only `node` on PATH - no npm, no node_modules, offline.
//
// Usage (run by the MSBuild task at consumer publish):
//   node rollup.bundled.mjs <loaderEntry.js> <outDir>
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

import { rollup } from '@rollup/wasm-node';
import nodeResolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';
import { join } from 'node:path';

const [input, outDir] = process.argv.slice(2);
if (!input || !outDir) {
    console.error('usage: node rollup.bundled.mjs <loaderEntry.js> <outDir>');
    process.exit(2);
}

// import.meta.url handling differs per output format:
//   - ES module: import.meta.url is native and correct -> leave it (do NOT shim;
//     document.currentScript is null in a module, so the shim would be wrong).
//   - classic (iife/umd/cjs): import.meta.url does not exist -> emit a shim that
//     resolves the script's own base URL in a window (<script>) and worker (self.location).
// Reuse the existing _framework output instead of emitting/copying assets.
// In a WasmBundlerFriendlyBootConfig dotnet.js graph every asset is a STATIC import:
//   import X_wasm from "./System.Private.CoreLib.8gxeyou5i5.wasm"   (assemblies + dotnet.native.wasm)
//   import Y_dat  from "./dotnet.timezones.blat"  / ICU ".dat"
// The default export of such an asset module is its URL. We resolve each to a tiny virtual
// module whose default export is the URL of the SAME-named file under _framework/, resolved
// at runtime against the bundle's own location (import.meta.url). Nothing is emitted or
// renamed, so the bundle references the real _framework assets that are already there.
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
            return `export default new URL(${JSON.stringify('_framework/' + name)}, import.meta.url).href;`;
        }
        return null;
    },
};

const importMetaPlugin = {
    name: 'worker-safe-import-meta-url',
    resolveImportMeta(property, { format }) {
        if (property === 'url' && (format === 'iife' || format === 'umd' || format === 'cjs')) {
            return `(typeof document!=='undefined'&&document.currentScript?document.currentScript.src:typeof self!=='undefined'&&self.location?self.location.href:typeof location!=='undefined'?location.href:'')`;
        }
        return null;
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
        importMetaPlugin,
        // Reference the existing _framework .wasm/.dat as-is (no emit, original names).
        frameworkAssetsPlugin,
        nodeResolve({ extensions: ['.js'] }),
        commonjs(),
    ],
    onwarn,
});

await bundle.write({ file: join(outDir, 'main.module.js'), format: 'es' });
await bundle.write({ file: join(outDir, 'main.classic.js'), format: 'iife' });
await bundle.close();

console.log(`SpawnJS bundle: wrote main.module.js + main.classic.js to ${outDir}`);
