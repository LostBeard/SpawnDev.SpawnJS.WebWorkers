namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// Runs <see cref="SubtleDigestCostProbe"/> in the PAGE - where SpawnDev.WebTorrent's v2 verify
    /// actually runs for a P2P torrent, since RTCPeerConnection is Window-scope and a peered torrent
    /// cannot be hosted in a worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐⭐ MEASURED 2026-09-14, Window, 4 MiB piece, 256 x 16 KiB leaves, best of 3:
    /// </para>
    /// <code>
    ///   wholeDigest       2.5 ms  (1600 MB/s, ONE call)   &lt;- the actual SHA-256
    ///   perLeaf          37.1 ms  (256 calls)
    ///     subArrayOnly   13.7 ms
    ///     digestCalls    20.6 ms
    ///   setLoop          11.3 ms
    ///   per-call overhead 0.071 ms/leaf
    /// </code>
    /// <para>
    /// ⭐ HASHING THE SAME BYTES AS 256 LEAVES COSTS 15x HASHING THEM AS ONE BUFFER, for identical
    /// cryptographic work. Of the 48.4 ms the per-leaf shape costs, 2.5 ms is crypto and ~95% is
    /// overhead. That is the ceiling on any batched-digest primitive - worth ~27 s on a 2.4 GB model.
    /// </para>
    /// <para>
    /// ⚠️ IT BOUNDS THE TOTAL, NOT THE RECOVERABLE PART. crypto.subtle.digest has no batch API, so a
    /// JS-side helper still issues 256 Promises - it removes the .NET/JS crossings, not the Promise
    /// machinery. This probe cannot split those two, so do not quote 95% as the expected win.
    /// </para>
    /// </remarks>
    public class SubtleDigestCostTests
    {
        [WebWorkerTest(Timeout = 120000)]
        public async Task SubtleDigest_PerLeafVsWhole_BoundsTheBatchedPrimitive()
        {
            var report = await SubtleDigestCostProbe.RunAsync();
            throw new Exception("DIGEST-COST || " + report);

            // The property the whole argument rests on: per-leaf hashing must cost materially more than
            // hashing the same bytes in one call. If that ever stops being true, the batched primitive has
            // no case and the numbers above are stale.
            var whole = ParseMs(report, "wholeDigest ");
            var perLeaf = ParseMs(report, "perLeaf ");
            if (whole <= 0 || perLeaf <= 0)
                throw new Exception($"probe did not report usable timings || {report}");
            if (perLeaf < whole * 3)
                throw new Exception(
                    $"per-leaf hashing ({perLeaf:F1} ms) is no longer materially worse than one call "
                    + $"({whole:F1} ms) - the case for a batched digest primitive is gone || {report}");
        }

        static double ParseMs(string report, string label)
        {
            var i = report.IndexOf(label, StringComparison.Ordinal);
            if (i < 0) return -1;
            var rest = report[(i + label.Length)..];
            var end = rest.IndexOf(' ');
            return end > 0 && double.TryParse(rest[..end], out var v) ? v : -1;
        }
    }
}
