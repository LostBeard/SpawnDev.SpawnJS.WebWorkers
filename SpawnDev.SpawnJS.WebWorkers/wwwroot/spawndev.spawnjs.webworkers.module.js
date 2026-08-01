import { } from './spawndev.spawnjs.webworkers.event-holder.js'
import { dotnet } from './_framework/dotnet.js'
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.withApplicationArguments("start").create();
await runMain();