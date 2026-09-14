using SpawnDev.SpawnJS.WebWorkers;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// Drives <see cref="OPFSStreamLayoutBenchmark"/> inside a real dedicated worker.
    /// </summary>
    /// <remarks>
    /// 🔴 THE WORKER IS THE POINT, not an implementation detail. <c>createSyncAccessHandle</c> exists ONLY
    /// in a DedicatedWorkerGlobalScope, so running this benchmark from the page would silently measure the
    /// async path and report it as though it were the fast one. The benchmark states
    /// <c>syncSupported</c> in its own output and this test asserts it, so a page-scope result can never be
    /// mistaken for a worker-scope result.
    /// </remarks>
    public class OPFSStreamLayoutTests(WebWorkerService webWorkerService)
    {
        const int ReadyTimeoutMs = 8000;

        async Task<WebWorker> GetReadyWorkerOrSkip()
        {
            if (!webWorkerService.WebWorkerSupported)
                throw new SkipTestException("Web Workers not supported by this host.");
            var worker = webWorkerService.GetWebWorkerSync();
            if (worker == null) throw new SkipTestException("GetWebWorkerSync returned null.");
            var ready = await Task.WhenAny(worker.WhenReady, Task.Delay(ReadyTimeoutMs));
            if (ready != worker.WhenReady)
            {
                worker.Dispose();
                throw new SkipTestException("Worker never became ready.");
            }
            return worker;
        }

        /// <summary>
        /// Reports the chunk-per-file vs one-file cost curve on the OPFSStream SYNC path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⭐⭐ MEASURED 2026-09-14, DedicatedWorkerGlobalScope, syncSupported=True, constant 64 MiB:
        /// </para>
        /// <code>
        ///   1024 x  64 KiB : chunk 2187 ms (open 1.60/entry) | whole  533 ms | 4.10x | 120 MB/s
        ///    256 x 256 KiB : chunk  610 ms (open 1.58/entry) | whole  214 ms | 2.85x | 301 MB/s
        ///     64 x   1 MiB : chunk  208 ms (open 1.76/entry) | whole   96 ms | 2.16x | 674 MB/s
        ///     16 x   4 MiB : chunk   94 ms (open 1.64/entry) | whole   70 ms | 1.35x | 936 MB/s
        /// </code>
        /// <para>
        /// ⭐ CHUNK-PER-FILE COSTS 1.35x AT PRODUCTION 4 MiB PIECES, not the 22-33x that chose the
        /// content-file layout. The Blob path measured 1.28x on the same configs, so both paths agree the
        /// layout barely matters once pieces are big. Sync per-open is a flat ~1.6 ms, which independently
        /// reproduces the prior session's <c>createSyncAccessHandle</c> = 1.66 ms.
        /// </para>
        /// <para>
        /// ⚠️ WHY THE OLD 22-33x DOES NOT REPRODUCE - it is the DENOMINATOR. That run's per-entry cost
        /// (346 ms / 128 = 2.70 ms) is close to this one's (2.14 ms); its one-file baseline read 8 MB at
        /// 533 MB/s where this reads 64 MB at 120 MB/s. An 8 MB file caches in a way 64 MB does not, so the
        /// ratio was inflated by an unusually fast baseline rather than by chunk files being slow. Compare
        /// the TERMS, not the ratio, when a ratio fails to reproduce.
        /// </para>
        /// <para>
        /// Numbers ride out on the exception message only when something fails: a worker's console does not
        /// reach the page, let alone the runner.
        /// </para>
        /// </remarks>
        [WebWorkerTest(Timeout = 300000)]
        public async Task OPFSStreamLayout_ChunkVsWhole_InDedicatedWorker()
        {
            using var worker = await GetReadyWorkerOrSkip();
            var report = await worker.Run(() => OPFSStreamLayoutBenchmark.RunAsync());

            if (string.IsNullOrWhiteSpace(report))
                throw new Exception("benchmark returned nothing");

            // A run that fell back to the async path is not the measurement this test claims to make.
            if (report.Contains("syncSupported=False", StringComparison.OrdinalIgnoreCase))
                throw new Exception(
                    "ran without sync access - createSyncAccessHandle is dedicated-worker only, so this did "
                    + $"NOT measure the sync path || {report}");

            if (!report.Contains("syncSupported=True")) throw new Exception($"unexpected report || {report}");
        }
    }
}
