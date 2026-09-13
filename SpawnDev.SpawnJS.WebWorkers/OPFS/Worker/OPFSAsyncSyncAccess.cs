using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS.Worker
{
    /// <summary>
    /// This can only run in a DedicatedWorkerScope
    /// </summary>
    public class OPFSAsyncSyncAccess : IOPFSAsyncSyncAccess
    {
        private static SpawnJSRuntime? JS => SpawnJSRuntime.Instance;
        /// <summary>
        /// Returns the current FileSystemSyncAccessHandle if available
        /// </summary>
        public FileSystemSyncAccessHandle? SyncAccessHandle => _syncAccessHandle;
        FileSystemSyncAccessHandle? _syncAccessHandle = null;
        /// <summary>
        /// The file's current length.<br/>
        /// Returns 0 if the file is not open.
        /// </summary>
        public long Length => _syncAccessHandle?.GetSize() ?? 0;
        /// <summary>
        /// Returns true if the file is open
        /// </summary>
        public bool IsOpen => _syncAccessHandle != null;
        /// <summary>
        /// Opens the sync handle and returns the file's current size
        /// </summary>
        /// <returns>The file's current size</returns>
        public async Task<long> OpenAsync(FileSystemFileHandle fileHandle, bool truncate)
        {
            if (_syncAccessHandle != null) throw new Exception("Already open");
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            // Get sync access handle
            _syncAccessHandle = await fileHandle.CreateSyncAccessHandle(new FileSystemSyncAccessOptions { Mode = "readwrite-unsafe" });
            if (truncate)
            {
                _syncAccessHandle.Truncate(0);
            }
            var size = _syncAccessHandle.GetSize();
            return size;
        }
        /// <summary>
        /// Opens the sync handle and returns the file's current size
        /// </summary>
        /// <returns>The file's current size</returns>
        public async Task<long> OpenAsync(FileSystemDirectoryHandle root, string name, FileMode fileMode)
        {
            if (_syncAccessHandle != null) throw new Exception("Already open");
            // Get handle to draft file
            FileSystemFileHandle? fileHandle = null;
            try
            {
                fileHandle = await root.GetFileHandle(name, false);
            }
            catch { }
            var truncate = false;
            switch (fileMode)
            {
                case FileMode.CreateNew:
                    // Creates a new file. An exception is raised if the file already exists.
                    if (fileHandle != null)
                    {
                        fileHandle.Dispose();
                        throw new Exception($"Failed: {fileMode}. Already exists.");
                    }
                    fileHandle = await root.GetFileHandle(name, true);
                    break;
                case FileMode.Create:
                    // Creates a new file. If the file already exists, it is overwritten.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetFileHandle(name, true);
                    }
                    truncate = true;
                    break;
                case FileMode.Open:
                    // Opens an existing file. An exception is raised if the file does not exist.
                    if (fileHandle == null)
                    {
                        throw new FileNotFoundException();
                    }
                    break;
                case FileMode.OpenOrCreate:
                    // Opens the file if it exists. Otherwise, creates a new file.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetFileHandle(name, true);
                    }
                    break;
                case FileMode.Truncate:
                    // Opens an existing file. Once opened, the file is truncated so that its
                    // size is zero bytes. The calling process must open the file with at least
                    // WRITE access. An exception is raised if the file does not exist.
                    if (fileHandle == null)
                    {
                        throw new FileNotFoundException();
                    }
                    truncate = true;
                    break;
                case FileMode.Append:
                    // Opens the file if it exists and seeks to the end.  Otherwise,
                    // creates a new file.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetFileHandle(name, true);
                    }
                    // seekToEnd = true - handled by the side that tracks position
                    break;
            }
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            // Get sync access handle
            _syncAccessHandle = await fileHandle.CreateSyncAccessHandle(new FileSystemSyncAccessOptions { Mode = "readwrite-unsafe" });
            if (truncate)
            {
                _syncAccessHandle.Truncate(0);
            }
            var size = _syncAccessHandle.GetSize();
            return size;
        }
        /// <summary>
        /// Opens the sync handle and returns the file's current size
        /// </summary>
        /// <returns>The file's current size</returns>
        public async Task<long> OpenAsync(string path, FileMode fileMode)
        {
            if (_syncAccessHandle != null) throw new Exception("Already open");
            using var navigator = JS!.Get<Navigator>("navigator");
            // Get handle to draft file
            using var root = await navigator.Storage.GetDirectory();
            var fileHandle = await root.GetPathFileHandle(path, false);
            var truncate = false;
            switch (fileMode)
            {
                case FileMode.CreateNew:
                    // Creates a new file. An exception is raised if the file already exists.
                    if (fileHandle != null)
                    {
                        fileHandle.Dispose();
                        throw new Exception($"Failed: {fileMode}. Already exists.");
                    }
                    fileHandle = await root.GetPathFileHandle(path, true);
                    break;
                case FileMode.Create:
                    // Creates a new file. If the file already exists, it is overwritten.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetPathFileHandle(path, true);
                    }
                    truncate = true;
                    break;
                case FileMode.Open:
                    // Opens an existing file. An exception is raised if the file does not exist.
                    if (fileHandle == null)
                    {
                        throw new FileNotFoundException();
                    }
                    break;
                case FileMode.OpenOrCreate:
                    // Opens the file if it exists. Otherwise, creates a new file.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetPathFileHandle(path, true);
                    }
                    break;
                case FileMode.Truncate:
                    // Opens an existing file. Once opened, the file is truncated so that its
                    // size is zero bytes. The calling process must open the file with at least
                    // WRITE access. An exception is raised if the file does not exist.
                    if (fileHandle == null)
                    {
                        throw new FileNotFoundException();
                    }
                    truncate = true;
                    break;
                case FileMode.Append:
                    // Opens the file if it exists and seeks to the end.  Otherwise,
                    // creates a new file.
                    if (fileHandle == null)
                    {
                        fileHandle = await root.GetPathFileHandle(path, true);
                    }
                    // seekToEnd = true - handled by the side that tracks position
                    break;
            }
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            // Get sync access handle
            _syncAccessHandle = await fileHandle.CreateSyncAccessHandle(new FileSystemSyncAccessOptions { Mode = "readwrite-unsafe" });
            if (truncate)
            {
                _syncAccessHandle.Truncate(0);
            }
            var size = _syncAccessHandle.GetSize();
            return size;
        }
        /// <summary>
        /// Write data to the file
        /// </summary>
        public async Task<long> WriteUint8ArrayAsync([WorkerTransfer] Uint8Array srcBuffer, long destOffset = 0)
        {
            if (_syncAccessHandle == null) throw new Exception("File not open");
            return _syncAccessHandle.Write(srcBuffer, new FileSystemSyncReadWriteOptions { At = destOffset });
        }
        /// <summary>
        /// Read data from the file and returns as a Uint8Array
        /// </summary>
        [return: WorkerTransfer]
        public async Task<Uint8Array> ReadUint8ArrayAsync(long srcOffset, long count)
        {
            if (_syncAccessHandle == null) throw new Exception("File not open");
            var size = _syncAccessHandle.GetSize();
            var bytesLeft = Math.Max(size - srcOffset, 0);
            count = Math.Max(0, Math.Min(bytesLeft, count));
            var destBuffer = new Uint8Array(count);
            if (count > 0) _syncAccessHandle.Read(destBuffer, new FileSystemSyncReadWriteOptions { At = srcOffset });
            return destBuffer;
        }
        /// <summary>
        /// Read data from the file and returns as a Uint8Array
        /// </summary>
        [return: WorkerTransfer]
        public async Task<Uint8Array> ReadUint8ArrayAsync(long srcOffset = 0)
        {
            if (_syncAccessHandle == null) throw new Exception("File not open");
            var size = _syncAccessHandle.GetSize();
            var bytesLeft = Math.Max(size - srcOffset, 0);
            var destBuffer = new Uint8Array(bytesLeft);
            if (bytesLeft > 0) _syncAccessHandle.Read(destBuffer, new FileSystemSyncReadWriteOptions { At = srcOffset });
            return destBuffer;
        }
        /// <summary>
        /// Returns the file's size
        /// </summary>
        public async Task<long> GetSizeAsync()
        {
            if (_syncAccessHandle == null) throw new Exception("File not open");
            return _syncAccessHandle.GetSize();
        }
        /// <summary>
        /// Truncate the file
        /// </summary>
        public async Task TruncateAsync(long newSize)
        {
            if (_syncAccessHandle == null) throw new Exception("File not open");
            _syncAccessHandle.Truncate(newSize);
        }
        /// <summary>
        /// Flush
        /// </summary>
        public async Task FlushAsync()
        {
            if (_syncAccessHandle == null) return;
            _syncAccessHandle.Flush();
        }
        /// <summary>
        /// Close
        /// </summary>
        public async Task<bool> CloseAsync()
        {
            if (_syncAccessHandle == null) return false;
            _syncAccessHandle.Close();
            _syncAccessHandle.Dispose();
            _syncAccessHandle = null;
            return true;
        }
    }
}
