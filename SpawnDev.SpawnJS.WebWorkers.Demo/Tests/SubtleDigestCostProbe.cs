using System.Diagnostics;
using System.Security.Cryptography;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// How much of a per-leaf SubtleCrypto hashing loop is actual SHA-256, and how much is the .NET/JS
    /// crossing around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS EXISTS TO PRICE A LIBRARY CHANGE BEFORE MAKING IT. SpawnDev.WebTorrent's v2 piece verify
    /// spends 75% of its time in per-leaf work (measured: digestFire 45.6 ms + leafSet 16.3 ms of an
    /// 82.2 ms total), which suggests collapsing ~5 crossings per leaf into ~1 per piece via a batched
    /// digest primitive in SpawnDev.SpawnJS.Cryptography. But "75% is per-leaf" is NOT "75% is
    /// recoverable" - some of it is SHA-256 that has to happen regardless. Building the primitive first
    /// and measuring after would be the "an operation count is not a cost" mistake with extra steps.
    /// </para>
    /// <para>
    /// ⭐ IT NEEDS NO NEW LIBRARY CODE. Same bytes hashed, different CALL COUNTS: one digest over the
    /// whole buffer against N digests over its leaves. Cost that tracks the call count is per-call
    /// (crossing + per-digest finalisation); cost that stays flat is the per-byte compression a batched
    /// helper could never remove. Pre-slicing the views separates SubArray creation from the digest call.
    /// </para>
    /// <para>
    /// ⚠️ NOT A PERFECT FLOOR. One digest over 4 MiB is not identical work to 256 digests over 16 KiB
    /// each - every digest pads and finalises separately - so the single-call figure slightly understates
    /// the irreducible cost. It bounds it from below, which is the direction that matters when deciding
    /// whether a change is worth making: if the ceiling on savings is already small, stop.
    /// </para>
    /// </remarks>
    public static class SubtleDigestCostProbe
    {
        const int PieceBytes = 4 * 1024 * 1024;   // production torrent piece
        const int LeafBytes = 16 * 1024;          // BEP 52 merkle leaf
        const int Rounds = 3;                     // best-of, so one scheduling hiccup cannot set the number

        static SpawnJSRuntime JS => SpawnJSRuntime.Instance!;

        public static async Task<string> RunAsync()
        {
            var leaves = PieceBytes / LeafBytes;
            using var subtle = JS.Get<SubtleCrypto>("crypto.subtle");
            using var piece = new Uint8Array(PieceBytes);

            double whole = double.MaxValue, perLeaf = double.MaxValue,
                   preSliced = double.MaxValue, sliceOnly = double.MaxValue, setLoop = double.MaxValue;
            long hashedBytes = 0;

            for (var round = 0; round < Rounds; round++)
            {
                // (1) ONE digest over the whole piece - the per-byte floor, a single crossing.
                var sw = Stopwatch.StartNew();
                using (var ab = await subtle.Digest("SHA-256", piece)) { hashedBytes += piece.Length; }
                whole = Math.Min(whole, sw.Elapsed.TotalMilliseconds);

                // (2) PRODUCTION SHAPE: SubArray + digest per leaf, all in flight, then awaited.
                sw.Restart();
                var views = new Uint8Array[leaves];
                var tasks = new Task<ArrayBuffer>[leaves];
                for (var i = 0; i < leaves; i++)
                {
                    views[i] = piece.SubArray(i * LeafBytes, (i + 1) * LeafBytes);
                    tasks[i] = subtle.Digest("SHA-256", views[i]);
                }
                var bufs = await Task.WhenAll(tasks);
                perLeaf = Math.Min(perLeaf, sw.Elapsed.TotalMilliseconds);

                // (4) The leafSet term: wrap each digest and Set it into one collecting buffer, JS-side.
                sw.Restart();
                using (var all = new Uint8Array((long)leaves * 32))
                {
                    for (var i = 0; i < leaves; i++)
                        using (var ua = new Uint8Array(bufs[i]))
                            all.Set(ua, i * 32);
                }
                setLoop = Math.Min(setLoop, sw.Elapsed.TotalMilliseconds);
                foreach (var b in bufs) b.Dispose();

                // (3) Digest calls ONLY, over views created beforehand and not timed.
                sw.Restart();
                var tasks2 = new Task<ArrayBuffer>[leaves];
                for (var i = 0; i < leaves; i++) tasks2[i] = subtle.Digest("SHA-256", views[i]);
                var bufs2 = await Task.WhenAll(tasks2);
                preSliced = Math.Min(preSliced, sw.Elapsed.TotalMilliseconds);
                foreach (var b in bufs2) b.Dispose();

                // SubArray creation on its own, so (2) can be decomposed rather than guessed at.
                sw.Restart();
                var views2 = new Uint8Array[leaves];
                for (var i = 0; i < leaves; i++) views2[i] = piece.SubArray(i * LeafBytes, (i + 1) * LeafBytes);
                sliceOnly = Math.Min(sliceOnly, sw.Elapsed.TotalMilliseconds);
                foreach (var v in views2) v.Dispose();
                foreach (var v in views) v.Dispose();
            }

            // ── THE .NET ALTERNATIVE (TJ): marshal the piece in and hash it here instead ──
            //
            // The JS-side hashing exists to avoid a copy into .NET. That is a MEANS, not the goal - if
            // copying in and hashing here beats 256 JS digest calls, the copy is necessary rather than
            // wasteful. Unknown going in: .NET SHA-256 under WASM has no SHA-NI, so its raw rate could be
            // far below SubtleCrypto's 1600 MB/s and sink the idea outright. Measured, not assumed.
            double marshal = double.MaxValue, netPerLeaf = double.MaxValue, netWhole = double.MaxValue;
            byte[]? managed = null;
            for (var round = 0; round < Rounds; round++)
            {
                var sw2 = Stopwatch.StartNew();
                managed = piece.ReadBytes();                       // JS -> .NET, the whole 4 MiB
                marshal = Math.Min(marshal, sw2.Elapsed.TotalMilliseconds);

                sw2.Restart();
                var digests = new byte[leaves][];
                for (var i = 0; i < leaves; i++)
                    digests[i] = SHA256.HashData(managed.AsSpan(i * LeafBytes, LeafBytes));
                netPerLeaf = Math.Min(netPerLeaf, sw2.Elapsed.TotalMilliseconds);

                sw2.Restart();
                _ = SHA256.HashData(managed);                      // reference: one call, same bytes
                netWhole = Math.Min(netWhole, sw2.Elapsed.TotalMilliseconds);
            }
            if (managed == null || managed.Length != PieceBytes)
                throw new Exception($"marshalled {managed?.Length ?? -1} bytes, expected {PieceBytes}");

            if (hashedBytes != (long)PieceBytes * Rounds)
                throw new Exception($"hashed {hashedBytes} bytes, expected {(long)PieceBytes * Rounds}");

            var mb = PieceBytes / 1048576.0;
            // The ceiling on what a batched JS-side primitive could recover: everything the per-leaf shape
            // costs, minus the one-call floor. It cannot remove SHA-256 itself.
            var ceiling = perLeaf + setLoop - whole;
            var netTotal = marshal + netPerLeaf;
            var jsTotal = perLeaf + setLoop;
            return $"scope={JS.GlobalScopeName} piece={mb:F0}MB leaves={leaves} (best of {Rounds}) || "
                 + $"DOTNET marshal {marshal:F1} ms ({mb / (marshal / 1000.0):F0} MB/s) + perLeafHash "
                 + $"{netPerLeaf:F1} ms ({mb / (netPerLeaf / 1000.0):F0} MB/s) = {netTotal:F1} ms "
                 + $"[wholeHash {netWhole:F1} ms] | JS path {jsTotal:F1} ms | "
                 + $"DOTNET IS {(netTotal < jsTotal ? "FASTER" : "SLOWER")} by {Math.Abs(jsTotal - netTotal):F1} ms "
                 + $"({(netTotal > 0 ? jsTotal / netTotal : 0):F2}x) || "
                 + $"wholeDigest {whole:F1} ms ({mb / (whole / 1000.0):F0} MB/s, 1 call) | "
                 + $"perLeaf {perLeaf:F1} ms ({leaves} calls) | "
                 + $"digestCallsOnly {preSliced:F1} ms | subArrayOnly {sliceOnly:F1} ms | "
                 + $"setLoop {setLoop:F1} ms || "
                 + $"per-call overhead {(preSliced - whole) / leaves:F3} ms/leaf | "
                 + $"CEILING on a batched primitive {ceiling:F1} ms of {perLeaf + setLoop:F1} ms "
                 + $"({ceiling / (perLeaf + setLoop):P0})";
        }
    }
}
