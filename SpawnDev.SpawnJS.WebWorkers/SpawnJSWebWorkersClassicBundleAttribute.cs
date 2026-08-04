using System;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Emitted into the consuming app's assembly by the SpawnDev.SpawnJS.WebWorkers build targets
    /// when the bundled worker entrypoints (main.classic.js / main.module.js) were produced for the
    /// current build (both build and publish; controlled by the SpawnJSWebWorkersClassicBundle
    /// opt-out property).<br/>
    /// <br/>
    /// <see cref="WebWorkerService.NonModuleScriptAvailable"/> is initialized from this attribute at
    /// startup (a synchronous reflection read that works in every scope - Window, Worker,
    /// SharedWorker, ServiceWorker - with no DOM or fetch). When present and <see cref="Available"/>
    /// is true, WebWorkerService creates workers from the bundled entrypoints (defaulting to the
    /// non-module <c>main.classic.js</c>); otherwise it falls back to the legacy module worker script
    /// (<c>spawndev.spawnjs.webworkers.module.js</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class SpawnJSWebWorkersClassicBundleAttribute : Attribute
    {
        /// <summary>
        /// True when the bundle build produced the classic + module entrypoints for this app build.
        /// </summary>
        public bool Available { get; }

        /// <summary>
        /// Creates a new instance of <see cref="SpawnJSWebWorkersClassicBundleAttribute"/>.
        /// </summary>
        /// <param name="available">Whether the bundled entrypoints were produced for this build.</param>
        public SpawnJSWebWorkersClassicBundleAttribute(bool available)
        {
            Available = available;
        }
    }
}
