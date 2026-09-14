using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.WebWorkers.OPFS;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// Concurrent access to ONE OPFS file through <see cref="OPFSInPlaceStream"/>.
    /// <para>
    /// ⭐ ONE STREAM PER CONSUMER, NOT ONE STREAM PER FILE. An earlier version of this file asserted the
    /// opposite - a single shared stream that every reader and the writer had to coordinate a cursor
    /// through. TJ rejected that design: <i>"maybe 1 stream total per file is a bad idea. Stream is not
    /// shaped for it."</i> A cursor is per-reader state and cannot be shared coherently, so no amount of
    /// locking makes a shared <c>Position</c> mean anything.
    /// </para>
    /// <para>
    /// 🔴 AND THE PREMISE THAT MOTIVATED SHARING WAS ALREADY FALSE.
    /// <see cref="OPFSAsyncSyncAccess"/> opens with <c>Mode = "readwrite-unsafe"</c> (three call sites),
    /// so each stream gets its OWN <c>FileSystemSyncAccessHandle</c> on the same file. Nothing needs a
    /// registry, a key or a refcount - all of which the rejected design had started to grow.
    /// <c>createSyncAccessHandle</c> is paid per stream-open, and a torrent holds a handful of
    /// long-lived streams, so the per-piece cost that motivated ContentFiles never comes back.
    /// </para>
    /// <para>
    /// ⚠️ <c>readwrite-unsafe</c> buys concurrency by REMOVING the lock between handles. That is safe for
    /// a torrent only because each piece is written once, by one writer, to a disjoint range, and the
    /// bitfield gates reads until the piece is complete. Those are invariants the STORE guarantees; the
    /// API enforces none of them. A caller that writes overlapping ranges from two streams gets exactly
    /// the corruption it asked for.
    /// </para>
    /// </summary>
    public class OPFSInPlaceConcurrencyTests
    {
        const int Block = 64 * 1024;
        const int Blocks = 8;

        static SpawnJSRuntime JS => SpawnJSRuntime.Instance!;

        static void SkipIfUnsupported()
        {
            if (!OperatingSystem.IsBrowser())
                throw new SkipTestException("OPFS requires a browser host.");
            if (!OPFSInPlaceStream.Supported)
                throw new SkipTestException(
                    "OPFSInPlaceStream is not supported in this scope (needs Window, DedicatedWorker or SharedWorker).");
        }

        /// <summary>A block filled with a single recognisable byte, so a misplaced write is unambiguous.</summary>
        static byte[] BlockOf(byte value)
        {
            var b = new byte[Block];
            System.Array.Fill(b, value);
            return b;
        }

        static string NewName() => $"inplace-concurrency-{Guid.NewGuid():N}.bin";

        static async Task RemoveQuietlyAsync(string name)
        {
            try
            {
                using var navigator = JS.Get<Navigator>("navigator");
                using var storage = navigator.Storage;
                using var root = await storage.GetDirectory();
                await root.RemoveEntry(name);
            }
            catch { /* best effort cleanup */ }
        }

        static async Task WriteAtAsync(OPFSInPlaceStream s, long offset, byte[] data)
        {
            using var ua = new Uint8Array(data.Length);
            ua.WriteBytes(data);
            await s.WriteUint8ArrayAtAsync(ua, offset);
        }

        static void AssertAllBytes(byte[] actual, byte expected, string what)
        {
            if (actual.Length == 0)
                throw new Exception($"{what}: read returned 0 bytes, expected {Block}");
            for (var i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected)
                    throw new Exception(
                        $"{what}: byte {i} of {actual.Length} is 0x{actual[i]:X2}, expected 0x{expected:X2}");
            }
        }

        /// <summary>
        /// Two streams may be open on one file at once, and their cursors are independent.
        /// <para>
        /// This is the property the whole "one stream per consumer" design rests on. If
        /// <c>createSyncAccessHandle</c> were opened in the default <c>readwrite</c> mode, the SECOND
        /// open would throw <c>NoModificationAllowedError</c> and the design would be impossible - so
        /// this test fails loudly at the open, before it ever reaches the cursor assertions.
        /// </para>
        /// </summary>
        [WebWorkerTest(Timeout = 60000)]
        public async Task TwoStreamsOnOneFileHaveIndependentCursorsTest()
        {
            SkipIfUnsupported();
            var name = NewName();
            try
            {
                using (var seed = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Create, FileAccess.ReadWrite))
                {
                    await seed.SetLengthAsync(Block * Blocks);
                    for (var i = 0; i < Blocks; i++) await WriteAtAsync(seed, (long)i * Block, BlockOf((byte)(i + 1)));
                    await seed.FlushAsync();
                }

                // Both open on the SAME file. Under "readwrite" this second open throws.
                using var a = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Open, FileAccess.ReadWrite);
                using var b = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Open, FileAccess.ReadWrite);

                a.Seek(2L * Block, SeekOrigin.Begin);
                b.Seek(5L * Block, SeekOrigin.Begin);

                // b's Seek must not have moved a's cursor.
                if (a.Position != 2L * Block)
                    throw new Exception(
                        $"stream a Position is {a.Position}, expected {2L * Block} - a second stream's Seek moved it, " +
                        "so the two are sharing cursor state rather than each holding their own");

                using (var readA = await a.ReadUint8ArrayAsync(Block))
                    AssertAllBytes(readA.ReadBytes(), 3, "stream a read at block 2");
                using (var readB = await b.ReadUint8ArrayAsync(Block))
                    AssertAllBytes(readB.ReadBytes(), 6, "stream b read at block 5");

                // Each advanced only its own cursor, by its own read.
                if (a.Position != 3L * Block || b.Position != 6L * Block)
                    throw new Exception(
                        $"after reading, a.Position={a.Position} (expected {3L * Block}), " +
                        $"b.Position={b.Position} (expected {6L * Block})");
            }
            finally { await RemoveQuietlyAsync(name); }
        }

        /// <summary>
        /// A reader stream reads correct bytes from a file a SEPARATE writer stream is actively writing.
        /// This is reading a torrent while it downloads, which is the reason in-place access exists: a
        /// writable would hide every write until close.
        /// </summary>
        [WebWorkerTest(Timeout = 60000)]
        public async Task AReaderStreamReadsCorrectBytesWhileAWriterStreamWritesTest()
        {
            SkipIfUnsupported();
            var name = NewName();
            try
            {
                using var writer = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Create, FileAccess.ReadWrite);
                await writer.SetLengthAsync(Block * Blocks);

                // Block 0 is settled before the reader is opened: the value it must come back with.
                await WriteAtAsync(writer, 0, BlockOf(0xAA));
                await writer.FlushAsync();

                using var reader = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Open, FileAccess.Read);

                // Writer works on a disjoint range while the reader reads block 0. Nothing serialises
                // them, and they hold different sync handles, so this is the real interleaving.
                var write = WriteAtAsync(writer, (long)(Blocks - 1) * Block, BlockOf(0xBB));
                var read = reader.ReadUint8ArrayAtAsync(0, Block);
                await Task.WhenAll(write, read);

                using (var got = await read)
                    AssertAllBytes(got.ReadBytes(), 0xAA, "reader stream read block 0 during a write to the last block");

                // The writer's in-flight block is visible to the reader once flushed - no close needed.
                await writer.FlushAsync();
                using (var tail = await reader.ReadUint8ArrayAtAsync((long)(Blocks - 1) * Block, Block))
                    AssertAllBytes(tail.ReadBytes(), 0xBB,
                        "reader stream read the block the writer had just flushed (in-place writes must be " +
                        "visible to another handle without closing the writer)");
            }
            finally { await RemoveQuietlyAsync(name); }
        }

        /// <summary>
        /// The positional API never touches <see cref="OPFSInPlaceStream.Position"/>, so no interleaving
        /// can move an operation's offset. This is what a torrent store should call: it addresses by
        /// absolute offset already, so it needs no seek and pays one round trip instead of two.
        /// </summary>
        [WebWorkerTest(Timeout = 60000)]
        public async Task PositionalApiIsUnaffectedByInterleavingTest()
        {
            SkipIfUnsupported();
            var name = NewName();
            try
            {
                using var s = (OPFSInPlaceStream)await SyncWorkerOPFS.OpenInPlaceStream(
                    name, FileMode.Create, FileAccess.ReadWrite);
                await s.SetLengthAsync(Block * Blocks);

                var buffers = new List<Uint8Array>();
                var writes = new List<Task<long>>();
                try
                {
                    // Reversed, so offset order and issue order disagree.
                    for (var i = Blocks - 1; i >= 0; i--)
                    {
                        var ua = new Uint8Array(Block);
                        ua.WriteBytes(BlockOf((byte)(i + 1)));
                        buffers.Add(ua);
                        writes.Add(s.WriteUint8ArrayAtAsync(ua, (long)i * Block));
                    }
                    await Task.WhenAll(writes);
                }
                finally { foreach (var b in buffers) b.Dispose(); }
                await s.FlushAsync();

                // Interleave a Seek-based caller in as well: it must not be able to drag a positional
                // read off its offset.
                s.Seek(Block * 3, SeekOrigin.Begin);

                var reads = new List<Task<Uint8Array>>();
                for (var i = 0; i < Blocks; i++) reads.Add(s.ReadUint8ArrayAtAsync((long)i * Block, Block));
                var results = await Task.WhenAll(reads);
                try
                {
                    for (var i = 0; i < Blocks; i++)
                        AssertAllBytes(results[i].ReadBytes(), (byte)(i + 1), $"positional read of block {i}");
                }
                finally { foreach (var r in results) r.Dispose(); }
            }
            finally { await RemoveQuietlyAsync(name); }
        }
    }
}
