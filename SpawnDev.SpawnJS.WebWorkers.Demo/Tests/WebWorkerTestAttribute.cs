namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// Marks a method as a WebWorkers test.<br/>
    /// The method must return <see cref="Task"/> and take no parameters. Pass by returning normally,
    /// fail by throwing, skip by throwing <see cref="SkipTestException"/>.<br/>
    /// This mirrors the SpawnJS test harness so the same Playwright runner drives both.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class WebWorkerTestAttribute : Attribute
    {
        /// <summary>
        /// Milliseconds to wait before the test is reported as timed out. 0 uses the runner default.<br/>
        /// Note: .Net WASM is single threaded, so this catches a test that awaits forever, not one stuck
        /// in a tight synchronous loop.
        /// </summary>
        public int Timeout { get; set; }
    }

    /// <summary>
    /// Throw from a test to report it as skipped rather than failed - used when a browser capability the
    /// test needs (Web Workers, SharedArrayBuffer, cross-origin isolation) is not available.
    /// </summary>
    public class SkipTestException : Exception
    {
        /// <summary>
        /// New instance
        /// </summary>
        public SkipTestException(string reason) : base(reason) { }
    }
}
