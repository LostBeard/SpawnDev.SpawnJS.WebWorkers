namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Basic instance information
    /// </summary>
    public class ServiceCallDispatcherInfo
    {
        /// <summary>
        /// From the instance's SpawnJSRuntime.InstanceId property
        /// </summary>
        public string InstanceId { get; init; } = "";
        /// <summary>
        /// The Javascript globalThis class name<br/>
        /// - Window<br/>
        /// - DedicatedWorkerGlobalScope<br/>
        /// - SharedWorkerGlobalScope<br/>
        /// - ServiceWorkerGlobalScope
        /// </summary>
        public string GlobalThisTypeName { get; init; } = "";
    }
}
