// non-module (classic) loader for loading .Net wasm in non-module environment like in classic [Service|Deedicated|Shared]Worker scopes
// TODO - 2 options
// - Do runtime patching and loading to convert import calls into importScripts calls (handle import.meta, exports, etc via shims and text replacement)
// - Patch at build time and Load pre-patched runtime (build time patching is required for non-module ServiceWorker in browser extensions)

console.warn('non-module workers not currently supported');
