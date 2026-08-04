// Legacy module-worker fallback, used only when the SpawnJS.WebWorkers bundle was NOT produced
// (SpawnJSWebWorkersClassicBundle=false -> the app is not bundler-friendly). It imports the runtime
// entry by its plain name './_framework/dotnet.js', so it only works when asset fingerprinting is OFF.
// The default/supported path is the bundled entrypoints (main.classic.js / main.module.js), which
// reference the app's own (fingerprinted) _framework.
import { } from './spawndev.spawnjs.webworkers.event-holder.js'
import { dotnet } from './_framework/dotnet.js'
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.withApplicationArguments("start").create();
await runMain();