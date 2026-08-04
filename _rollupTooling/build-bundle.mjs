// build-bundle.mjs  (dev-only, run on the library side)
// Bundles spawnjs-bundle.mjs + @rollup/wasm-node + plugins into ONE self-contained
// file: ../buildcontent/rollup.bundled.mjs. The @rollup/wasm-node binding reads its
// wasm from disk via readFileSync(`${__dirname}/bindings_wasm_bg.wasm`); we inline
// that wasm as base64 so the result is a single file that runs with only `node`.
import esbuild from 'esbuild';
import { readFileSync, mkdirSync } from 'node:fs';

const WASM = 'node_modules/@rollup/wasm-node/dist/wasm-node/bindings_wasm_bg.wasm';
const wasmB64 = readFileSync(WASM).toString('base64');

const inlineWasmPlugin = {
    name: 'inline-rollup-wasm',
    setup(build) {
        build.onLoad({ filter: /bindings_wasm\.js$/ }, (args) => {
            let src = readFileSync(args.path, 'utf8');
            // Replace the runtime fs read of the wasm with an inlined base64 buffer.
            src = src.replace(
                /const wasmBytes = require\('fs'\)\.readFileSync\(wasmPath\);/,
                `const wasmBytes = Buffer.from(${JSON.stringify(wasmB64)}, 'base64');`
            );
            // Neutralize the now-unused __dirname-based path (avoids an ESM __dirname reference).
            src = src.replace(/const wasmPath = `[^`]*`;/, 'const wasmPath = "<inlined>";');
            return { contents: src, loader: 'js' };
        });
    },
};

const OUT = '../SpawnDev.SpawnJS.WebWorkers/buildcontent/rollup.bundled.mjs';
mkdirSync('../SpawnDev.SpawnJS.WebWorkers/buildcontent', { recursive: true });

await esbuild.build({
    entryPoints: ['spawnjs-bundle.mjs'],
    bundle: true,
    platform: 'node',
    format: 'esm',
    outfile: OUT,
    plugins: [inlineWasmPlugin],
    // wasm-node internals use CJS require(); provide it in the ESM output.
    banner: { js: "import { createRequire as __esbuildCreateRequire } from 'module';\nconst require = __esbuildCreateRequire(import.meta.url);" },
    logLevel: 'info',
});

console.log('wrote ' + OUT);
