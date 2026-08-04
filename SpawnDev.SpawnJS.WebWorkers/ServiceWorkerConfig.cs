using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Options for managing ServiceWorker registration
    /// </summary>
    public class ServiceWorkerConfig
    {
        /// <summary>
        /// The registration action to take when the app starts in a Window scope
        /// </summary>
        public ServiceWorkerStartupRegistration Register { get; set; } = ServiceWorkerStartupRegistration.Register;
        /// <summary>
        /// The service worker script URL. When null, WebWorkerService uses the default entrypoint: the bundled
        /// "main.classic.js" (or "main.module.js" when Options.Type == "module") if the SpawnJS.WebWorkers bundle
        /// was produced for this build, otherwise the legacy "spawndev.spawnjs.webworkers.module.js".<br/>
        /// </summary>
        public string? ScriptURL { get; set; }
        /// <summary>
        /// By default, this is "service-worker-assets.js"
        /// This should be the value from &lt;ServiceWorkerAssetsManifest&gt; in your project's .csproj file if different than the default
        /// </summary>
        public string? ServiceWorkerAssetsManifest { get; set; }
        /// <summary>
        /// This should be true if using &lt;ServiceWorkerAssetsManifest&gt; in your project's .csproj file
        /// </summary>
        public bool ImportServiceWorkerAssets { get; set; }
        /// <summary>
        /// Options used when registering a ServiceWorker via ServiceWorkerContainer.Register()
        /// </summary>
        public ServiceWorkerRegistrationOptions? Options { get; set; }
    }
}

