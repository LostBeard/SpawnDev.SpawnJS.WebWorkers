import { dotnet } from './_framework/dotnet.js'
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.withApplicationArguments("start").create();
await runMain();