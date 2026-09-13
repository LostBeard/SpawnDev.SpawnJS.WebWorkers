using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

public static class StreamThroughputTester
{
    /// <summary>
    /// Tests the write and read throughput of an already opened stream.
    /// </summary>
    /// <param name="getWriteStream">The write Stream getter for the test.</param>
    /// <param name="getReadStream">The read Stream getter for the test.</param>
    /// <param name="testSizeInBytes">Total amount of data to process (e.g., 50 * 1024 * 1024 for 50MB).</param>
    /// <param name="bufferSize">The size of the block chunk used for reading and writing (e.g., 4096 or 65536).</param>
    public static void RunThroughputTest(Func<Stream> getWriteStream, Func<Stream> getReadStream, long testSizeInBytes = 100 * 1024 * 1024, int bufferSize = 1024 * 1024 * 4)
    {
        var stream = getWriteStream();
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite) throw new InvalidOperationException("The provided stream does not support writing.");
        if (!stream.CanRead) throw new InvalidOperationException("The provided stream does not support reading.");
        if (!stream.CanSeek) throw new InvalidOperationException("The stream must support seeking to reset between write and read tests.");

        // Initialize a dummy buffer populated with random bytes to prevent OS compression optimization
        byte[] buffer = new byte[bufferSize];
        Random.Shared.NextBytes(buffer);

        Console.WriteLine($"--- Starting Sync Throughput Test - Total Size: {testSizeInBytes / (1024.0 * 1024.0):F2} MB - Buffer Size: {bufferSize / (1024.0 * 1024.0):F2} MB ---");

        // ==========================================
        // 1. WRITE SPEED TEST
        // ==========================================
        long bytesWritten = 0;
        Stopwatch writeTimer = Stopwatch.StartNew();

        while (bytesWritten < testSizeInBytes)
        {
            int bytesToWrite = (int)Math.Min(bufferSize, testSizeInBytes - bytesWritten);
            stream.Write(buffer, 0, bytesToWrite);
            bytesWritten += bytesToWrite;
        }

        // Force underlying OS buffers to flush to disk to ensure accurate measurements
        stream.Flush();
        writeTimer.Stop();

        double writeSeconds = writeTimer.Elapsed.TotalSeconds;
        double writeMbps = (bytesWritten / (1024.0 * 1024.0)) / writeSeconds;
        Console.WriteLine($"Write Speed: {writeMbps:F2} MB/s (Took {writeSeconds:F4} seconds)");

        stream = getReadStream();
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new InvalidOperationException("The provided stream does not support reading.");
        if (!stream.CanSeek) throw new InvalidOperationException("The stream must support seeking to reset between write and read tests.");

        // ==========================================
        // RESET POSITION
        // ==========================================
        stream.Position = 0;

        // ==========================================
        // 2. READ SPEED TEST
        // ==========================================
        long bytesReadTotal = 0;
        Stopwatch readTimer = Stopwatch.StartNew();

        while (bytesReadTotal < testSizeInBytes)
        {
            int bytesToRead = (int)Math.Min(bufferSize, testSizeInBytes - bytesReadTotal);
            int bytesRead = stream.Read(buffer, 0, bytesToRead);

            if (bytesRead == 0) break; // End of stream reached unexpectedly
            bytesReadTotal += bytesRead;
        }

        readTimer.Stop();

        double readSeconds = readTimer.Elapsed.TotalSeconds;
        double readMbps = (bytesReadTotal / (1024.0 * 1024.0)) / readSeconds;
        Console.WriteLine($"Read Speed:  {readMbps:F2} MB/s (Took {readSeconds:F4} seconds)");
        Console.WriteLine("---------------------------------------\n");
    }
    /// <summary>
    /// Asynchronously tests the write and read throughput of an already opened stream.
    /// </summary>
    /// <param name="getWriteStream">The write Stream getter for the test.</param>
    /// <param name="getReadStream">The read Stream getter for the test.</param>
    /// <param name="testSizeInBytes">Total amount of data to process (e.g., 50 * 1024 * 1024 for 50MB).</param>
    /// <param name="bufferSize">The size of the block chunk used for reading and writing (e.g., 65536).</param>
    public static async Task RunThroughputTestAsync(Func<Task<Stream>> getWriteStream, Func<Task<Stream>> getReadStream, long testSizeInBytes = 100 * 1024 * 1024, int bufferSize = 1024 * 1024 * 4)
    {
        var stream = await getWriteStream();
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite) throw new InvalidOperationException("The provided stream does not support writing.");
        if (!stream.CanSeek) throw new InvalidOperationException("The stream must support seeking to reset between write and read tests.");

        // Initialize a dummy buffer populated with random bytes to prevent OS compression optimization
        byte[] buffer = new byte[bufferSize];
        Random.Shared.NextBytes(buffer);

        Console.WriteLine($"--- Starting Async Throughput Test - Total Size: {testSizeInBytes / (1024.0 * 1024.0):F2} MB - Buffer Size: {bufferSize / (1024.0 * 1024.0):F2} MB ---");

        // ==========================================
        // 1. ASYNC WRITE SPEED TEST
        // ==========================================
        long bytesWritten = 0;
        Stopwatch writeTimer = Stopwatch.StartNew();

        while (bytesWritten < testSizeInBytes)
        {
            int bytesToWrite = (int)Math.Min(bufferSize, testSizeInBytes - bytesWritten);

            // Pass a Memory<byte> slice to avoid older array allocation allocations
            await stream.WriteAsync(buffer.AsMemory(0, bytesToWrite));
            bytesWritten += bytesToWrite;
        }

        // Force underlying hardware/OS buffers to flush asynchronously
        await stream.FlushAsync();
        writeTimer.Stop();
        await stream.DisposeAsync();

        double writeSeconds = writeTimer.Elapsed.TotalSeconds;
        double writeMbps = (bytesWritten / (1024.0 * 1024.0)) / writeSeconds;
        Console.WriteLine($"Async Write Speed: {writeMbps:F2} MB/s (Took {writeSeconds:F4} seconds)");


        stream = await getReadStream();
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new InvalidOperationException("The provided stream does not support reading.");
        if (!stream.CanSeek) throw new InvalidOperationException("The stream must support seeking to reset between write and read tests.");

        // ==========================================
        // RESET POSITION
        // ==========================================
        stream.Position = 0;

        if (stream.Length != testSizeInBytes) throw new Exception($"Read stream size does not match bytes written: {stream.Length} != {testSizeInBytes}");

        // ==========================================
        // 2. ASYNC READ SPEED TEST
        // ==========================================
        long bytesReadTotal = 0;
        Stopwatch readTimer = Stopwatch.StartNew();

        while (bytesReadTotal < testSizeInBytes)
        {
            int bytesToRead = (int)Math.Min(bufferSize, testSizeInBytes - bytesReadTotal);

            // Read using modern Memory<byte> overload
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead));

            if (bytesRead == 0) break; // End of stream reached unexpectedly
            bytesReadTotal += bytesRead;
        }

        readTimer.Stop();

        await stream.DisposeAsync();

        double readSeconds = readTimer.Elapsed.TotalSeconds;
        double readMbps = (bytesReadTotal / (1024.0 * 1024.0)) / readSeconds;
        Console.WriteLine($"Async Read Speed:  {readMbps:F2} MB/s (Took {readSeconds:F4} seconds)");
        Console.WriteLine("-------------------------------------------\n");
    }
}
