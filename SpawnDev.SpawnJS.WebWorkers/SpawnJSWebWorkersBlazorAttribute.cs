using System;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Emitted into the consuming app's assembly by the SpawnDev.SpawnJS.WebWorkers build targets
    /// when the app is a Blazor WebAssembly app (Microsoft.NET.Sdk.BlazorWebAssembly, or
    /// <c>SpawnJSWebWorkersBlazor=true</c>).<br/>
    /// <br/>
    /// <see cref="WebWorkerService.IsBlazorApp"/> is initialized from this attribute at startup.
    /// When present and <see cref="Enabled"/> is true, workers boot via the Blazor entry scripts
    /// (<c>spawndev.spawnjs.webworkers.js</c> / <c>spawndev.spawnjs.webworkers.module.js</c>) that
    /// load faux-env + <c>blazor.webassembly.js</c> and call <c>Blazor.start()</c> before managed
    /// <c>WebAssemblyHostBuilder.CreateDefault</c>. Plain .Net WASM apps omit this attribute and
    /// use the classic/module bundle (<c>main.classic.js</c> / <c>main.module.js</c>) instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class SpawnJSWebWorkersBlazorAttribute : Attribute
    {
        /// <summary>
        /// True when this app build targets Blazor WebAssembly worker boot.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Creates a new instance of <see cref="SpawnJSWebWorkersBlazorAttribute"/>.
        /// </summary>
        /// <param name="enabled">Whether Blazor worker boot is enabled for this build.</param>
        public SpawnJSWebWorkersBlazorAttribute(bool enabled)
        {
            Enabled = enabled;
        }
    }
}
