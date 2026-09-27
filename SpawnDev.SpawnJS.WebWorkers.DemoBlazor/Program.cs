using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.WebWorkers;
using SpawnDev.SpawnJS.WebWorkers.DemoBlazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSpawnJSRuntime(out var JS);

// register WebWorkerService
builder.Services.AddWebWorkerService();

if (JS.IsWindow)
{
    builder.RootComponents.Add<App>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
}

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().SpawnJSRunAsync();
