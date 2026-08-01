using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Options used for CreateWebWorker
    /// </summary>
    public class WebWorkerOptions
    {
        /// <summary>
        /// WorkerOptions
        /// </summary>
        public WorkerOptions? WorkerOptions { get; set; }
        /// <summary>
        /// The URL to the worker script to load.<br/>
        /// Defaults to: 
        /// module - "spawndev.spawnjs.webworkers.module.js"<br/>
        /// classic - "spawndev.spawnjs.webworkers.js"<br/>
        /// </summary>
        public string? ScriptUrl { get; set; } = null;
        /// <summary>
        /// Additional query parameters to add to the worker script URL.<br/>
        /// </summary>
        public Dictionary<string, string>? QueryParams { get; set; } = null;
    }
}
