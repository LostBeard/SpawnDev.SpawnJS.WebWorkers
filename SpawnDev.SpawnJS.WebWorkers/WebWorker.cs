using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// WebWorker
    /// </summary>
    public class WebWorker : ServiceCallDispatcher, IDisposable
    {
        public static bool Supported;
        static WebWorker()
        {
            Supported = JS!.Has("Worker");
        }
        Worker _worker;
        public WebWorker(Worker worker, IBackgroundServiceManager webAssemblyServices) : base(webAssemblyServices, worker)
        {
            _worker = worker;
        }
        /// <summary>
        /// Called when being Disposed, before the disposal
        /// </summary>
        public event Action<WebWorker> OnDisposing;
        /// <summary>
        /// Returns true if disposal has started
        /// </summary>
        public bool IsDisposing { get; protected set; } = false;
        protected override void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            if (IsDisposing) return;
            IsDisposing = true;
            OnDisposing?.Invoke(this);
            try
            {
                _worker?.Terminate();
            }
            catch { }
            _worker?.Dispose();
            base.Dispose(disposing);
        }
    }
}
