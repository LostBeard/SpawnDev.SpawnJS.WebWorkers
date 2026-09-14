using System.Diagnostics;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// What chunk-per-file actually costs against one-big-file, measured through
    /// <see cref="OPFSStream"/> in a DEDICATED WORKER - the configuration a shipped torrent store would
    /// really run in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 WHY HERE AND NOT IN SpawnDev.WebTorrent. <c>createSyncAccessHandle</c> exists ONLY in a
    /// DedicatedWorkerGlobalScope, so the fast path can only be measured from inside a worker. WebTorrent
    /// deliberately does not depend on SpawnDev.SpawnJS.WebWorkers - it runs in desktop apps too - so the
    /// measurement lives in the repo that already has a worker harness. Nothing here needs WebTorrent:
    /// <see cref="OPFSStream"/> is plain SpawnJS.
    /// </para>
    /// <para>
    /// ⭐ THE CONFIGS MATCH THE BLOB-PATH RUN EXACTLY (1024/256/64/16 entries, constant 64 MiB), so the two
    /// are directly comparable instead of stitched together across sessions. Every config moves the SAME
    /// bytes and varies only entry count: cost that tracks entry count is per-OPEN, cost that stays flat is
    /// per-BYTE.
    /// </para>
    /// <para>
    /// ⚠️ Bulk bytes never enter the managed heap. One <see cref="Uint8Array"/> payload per config is
    /// reused, and reads come back through <c>ReadUint8ArrayAsync</c>. A benchmark that marshalled each
    /// buffer would be measuring the marshal.
    /// </para>
    /// </remarks>
    public static class OPFSStreamLayoutBenchmark
    {
        const string BenchRoot = "_opfsstream-layout-bench";

        /// <summary>Constant total bytes per config; only the entry count changes.</summary>
        static readonly (int Count, int Bytes)[] Configs =
        {
            (1024, 65536),     //  64 KiB
            ( 256, 262144),    // 256 KiB
            (  64, 1048576),   //   1 MiB
            (  16, 4194304),   //   4 MiB - production torrent piece size
        };

        static SpawnJSRuntime JS => SpawnJSRuntime.Instance!;

        /// <summary>
        /// Runs the sweep and returns the report. Returns a STRING rather than a record so the result
        /// crosses the worker boundary through the plainest marshaller there is - the benchmark should not
        /// be measuring, or tripping over, its own result serialization.
        /// </summary>
        public static async Task<string> RunAsync()
        {
            var lines = new List<string>
            {
                $"scope={JS.GlobalScopeName} syncSupported={OPFSStream.SyncSupported}"
            };

            // Say it loudly rather than quietly measuring the async path and reporting it as the fast one.
            if (!OPFSStream.SyncSupported)
                lines.Add("!! SYNC UNAVAILABLE - these are async-path numbers, NOT the sync path !!");

            try
            {
                double? prevThroughput = null;
                foreach (var (count, bytes) in Configs)
                {
                    var (line, thru) = await MeasureOneAsync(count, bytes);
                    lines.Add(line);
                    // Every config moves the SAME bytes in progressively larger reads, so throughput must
                    // climb. Measured 120 -> 301 -> 674 -> 936 MB/s. If it ever stops climbing, the
                    // read-size effect these conclusions rest on is gone and the numbers must not be
                    // quoted as if it held.
                    if (prevThroughput is { } prev && thru <= prev)
                        throw new Exception(
                            $"read throughput did not improve at {bytes} B reads ({thru:F0} MB/s) over the "
                            + $"previous, smaller reads ({prev:F0} MB/s) - the read-size effect is gone");
                    prevThroughput = thru;
                }
            }
            finally
            {
                try
                {
                    using var navigator = JS.Get<Navigator>("navigator");
                    using var storage = navigator.Storage;
                    using var root = await storage.GetDirectory();
                    await root.RemoveEntry(BenchRoot, recursive: true);
                }
                catch { /* leftover bench dir is noise, not a result */ }
            }
            return string.Join(" || ", lines);
        }

        static async Task<(string Line, double ThroughputMBs)> MeasureOneAsync(int count, int bytes)
        {
            var chunkDir = $"{BenchRoot}/chunks-{count}-{bytes}";
            var wholePath = $"{BenchRoot}/whole-{count}-{bytes}.bin";

            // ONE payload, reused for every write. Allocated JS-side and never marshalled.
            using var payload = new Uint8Array(bytes);

            // ── write both layouts. TIMED, because an "unpack after 100%" pass is exactly this: read every
            // chunk and write it into a content file. Its cost decides whether a one-time unpack is cheap.
            var chunkWrite = new Stopwatch();
            var wholeWrite = new Stopwatch();
            chunkWrite.Start();
            for (var i = 0; i < count; i++)
            {
                await using var w = await OPFSStream.OpenPath(
                    $"{chunkDir}/{i}.bin", FileMode.Create, FileAccess.Write);
                await w.WriteUint8ArrayAsync(payload);
            }
            chunkWrite.Stop();
            wholeWrite.Start();
            {
                await using var w = await OPFSStream.OpenPath(wholePath, FileMode.Create, FileAccess.Write);
                for (var i = 0; i < count; i++) await w.WriteUint8ArrayAsync(payload);
            }
            wholeWrite.Stop();

            // ── read: chunk-per-file, one OPEN per entry ──
            var chunkOpen = new Stopwatch();
            var chunkRead = new Stopwatch();
            long chunkBytes = 0;
            for (var i = 0; i < count; i++)
            {
                chunkOpen.Start();
                var r = await OPFSStream.OpenPath($"{chunkDir}/{i}.bin", FileMode.Open, FileAccess.Read);
                chunkOpen.Stop();
                chunkRead.Start();
                using (var got = await r.ReadUint8ArrayAsync(bytes)) chunkBytes += got.Length;
                chunkRead.Stop();
                await r.DisposeAsync();
            }

            // ── read: one file, ONE open, same number of same-sized reads ──
            var wholeOpen = new Stopwatch();
            var wholeRead = new Stopwatch();
            long wholeBytes = 0;
            {
                wholeOpen.Start();
                var r = await OPFSStream.OpenPath(wholePath, FileMode.Open, FileAccess.Read);
                wholeOpen.Stop();
                for (var i = 0; i < count; i++)
                {
                    wholeRead.Start();
                    using (var got = await r.ReadUint8ArrayAsync(bytes)) wholeBytes += got.Length;
                    wholeRead.Stop();
                }
                await r.DisposeAsync();
            }

            var chunkTotal = chunkOpen.Elapsed.TotalMilliseconds + chunkRead.Elapsed.TotalMilliseconds;
            var wholeTotal = wholeOpen.Elapsed.TotalMilliseconds + wholeRead.Elapsed.TotalMilliseconds;
            var mb = (double)count * bytes / 1048576.0;
            var thru = wholeRead.Elapsed.TotalMilliseconds > 0
                ? mb / (wholeRead.Elapsed.TotalMilliseconds / 1000.0) : 0;

            // A run that read nothing would otherwise look like the fastest run of all.
            var expected = (long)count * bytes;
            if (chunkBytes != expected || wholeBytes != expected)
                throw new Exception(
                    $"{count} x {bytes} B: read {chunkBytes} (chunks) / {wholeBytes} (whole), expected "
                    + $"{expected} each - the timings describe reads that did not happen");

            var line = $"{count} x {bytes} B ({mb:F0} MB): "
                 + $"chunk {chunkTotal:F0} ms (open {chunkOpen.Elapsed.TotalMilliseconds:F0} "
                 + $"[{chunkOpen.Elapsed.TotalMilliseconds / count:F2}/entry] + read {chunkRead.Elapsed.TotalMilliseconds:F0}) "
                 + $"| whole {wholeTotal:F0} ms (open {wholeOpen.Elapsed.TotalMilliseconds:F1} + read {wholeRead.Elapsed.TotalMilliseconds:F0}) "
                 + $"| ratio {(wholeTotal > 0 ? chunkTotal / wholeTotal : 0):F2}x "
                 + $"| whole-read {thru:F0} MB/s | bytes {chunkBytes}/{wholeBytes} "
                 + $"|| WRITE chunk {chunkWrite.Elapsed.TotalMilliseconds:F0} ms "
                 + $"({mb / (chunkWrite.Elapsed.TotalMilliseconds / 1000.0):F0} MB/s) "
                 + $"whole {wholeWrite.Elapsed.TotalMilliseconds:F0} ms "
                 + $"({mb / (wholeWrite.Elapsed.TotalMilliseconds / 1000.0):F0} MB/s)";
            return (line, thru);
        }
    }
}
