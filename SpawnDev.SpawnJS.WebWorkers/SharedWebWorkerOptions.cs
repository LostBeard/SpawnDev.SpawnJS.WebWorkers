using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Options used for CreateWebWorker
    /// </summary>
    public class SharedWebWorkerOptions
    {
        /// <summary>
        /// WorkerOptions
        /// </summary>
        public SharedWorkerOptions? WorkerOptions { get; set; }
        /// <summary>
        /// The URL to the worker script to load.<br/>
        /// Defaults to: 
        /// module - "spawndev.spawnjs.webworkers.module.js"<br/>
        /// classic - "spawndev.spawnjs.webworkers.js"<br/>
        /// </summary>
        public string? ScriptUrl { get; set; } = null;
    }
}
