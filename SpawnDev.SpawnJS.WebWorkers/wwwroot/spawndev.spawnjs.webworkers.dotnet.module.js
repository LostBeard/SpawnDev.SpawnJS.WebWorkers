// Legacy module-worker fallback for plain .Net WASM apps (NOT Blazor) when the
// SpawnJS.WebWorkers classic/module bundle was NOT produced
// (SpawnJSWebWorkersClassicBundle=false). Imports the runtime entry by its plain
// name './_framework/dotnet.js', so it only works when asset fingerprinting is OFF.
// The default/supported path for plain .Net WASM is the bundled entrypoints
// (main.classic.js / main.module.js). Blazor apps use spawndev.spawnjs.webworkers.js
// / spawndev.spawnjs.webworkers.module.js instead (faux-env + Blazor.start).
import { } from './spawndev.spawnjs.webworkers.event-holder.js'
import { dotnet } from './_framework/dotnet.js'
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.withApplicationArguments("start").create();
await runMain();
