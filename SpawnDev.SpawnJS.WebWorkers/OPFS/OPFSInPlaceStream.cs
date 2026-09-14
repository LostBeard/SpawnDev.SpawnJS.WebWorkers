using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;
using SpawnDev.SpawnJS.WebWorkers.OPFS.Worker;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS
{
    /// <summary>
    /// Access OPFS asynchronously using the synchronous API from Window, DedicatedWorker, and SharedWorker scopes.<br/>
    /// If not already running in a dedicated worker and the current scope can start a dedicateed worker<br/>
    /// OPFSInPlaceStream.Open() will start a new dedicated worker is one is not already working and use it for OPFS access.<br/>
    /// Purpose:<br/>
    /// This allows in-place write access to OPFS files which is only possible via FileSystemSyncAccessHandle.
    /// </summary>
    public class OPFSInPlaceStream : JSReadWriteStreamBase
    {
        static GlobalScope[] _envSupported = [GlobalScope.Window, GlobalScope.DedicatedWorker, GlobalScope.SharedWorker];
        /// <summary>
        /// Returns true if this environment can use this class
        /// </summary>
        public static bool Supported => WebWorkerService != null && _envSupported.Any(o => o == WebWorkerService.GlobalScope);
        private static WebWorker? _webWorker = null;
        private static WebWorkerService? WebWorkerService => WebWorkerService.Instance;
        private static SpawnJSRuntime? JS => WebWorkerService.Instance?.JS;
        /// <inheritdoc/>
        public override bool CanWriteSync => _canWriteSync;
        private bool _canWriteSync = false;
        /// <inheritdoc/>
        public override bool CanReadSync => _canReadSync;
        private bool _canReadSync = false;
        /// <inheritdoc/>
        public override bool CanRead => _canRead;
        private bool _canRead = false;
        /// <inheritdoc/>
        public override bool CanWrite => _canWrite;
        private bool _canWrite = false;
        /// <inheritdoc/>
        public override bool CanSeek => _canSeek;
        private bool _canSeek = true;
        /// <inheritdoc/>
        public override long Length => _length;
        private long _length = 0;
        /// <inheritdoc/>
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        private long _position = 0;
        /// <summary>
        /// Returns true of the file is open
        /// </summary>
        public bool IsOpen { get; private set; }
        /// <summary>
        /// Returns true if the file is open and direct sync Stream access is possible
        /// </summary>
        public bool IsSyncOpen => IsOpen && _syncAccessHandle != null;
        private IOPFSAsyncSyncAccess _handleManager;
        /// <summary>
        /// If _handleManager is running in this instance and not a worker this returns the sync handle which enables<br/>
        /// synchronous stream access
        /// </summary>
        private FileSystemSyncAccessHandle? _syncAccessHandle => !_supportSyncAccess ? null : _handleManager?.SyncAccessHandle;
        /// <summary>
        /// Fires when this stream is disposed
        /// </summary>
        public event Action<OPFSInPlaceStream>? OnDisposed;
        private static Dictionary<string, OPFSInPlaceStream> WorkerStreams { get; } = new Dictionary<string, OPFSInPlaceStream>();
        private bool _supportSyncAccess { get; }
        private OPFSInPlaceStream(IOPFSAsyncSyncAccess handleManager, bool supportSyncAccess)
        {
            _handleManager = handleManager;
            _supportSyncAccess = supportSyncAccess;
        }
        private static SemaphoreSlim _openLimiter = new SemaphoreSlim(1);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="root">Root directory handle</param>
        /// <param name="path">The file entry name to open</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="cancellationToken">FileAccess</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static async Task<OPFSInPlaceStream> OpenPath(FileSystemDirectoryHandle root, string path, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimiter = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimiter = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSAsyncSyncAccess();
                    OPFSInPlaceStream? ret = null;
                    try
                    {
                        ret = new OPFSInPlaceStream(fileManager, true);
                        await ret.OpenPathInternal(root, path, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret?.Dispose();
                        throw;
                    }
                    return ret;
                }
                else if (JS.IsWindow || JS.IsSharedWorkerGlobalScope)
                {
                    // this scope does not support sync access but can start a dedicated worker that does
                    // OPFSStreamWorkerService will run in the dedicated worker scope
                    _webWorker ??= await WebWorkerService.GetWebWorker();
                    var serviceKey = Guid.NewGuid().ToString();
                    await _webWorker!.New<IOPFSAsyncSyncAccess>(serviceKey, () => new OPFSAsyncSyncAccess());
                    var fileManager = _webWorker.GetKeyedService<IOPFSAsyncSyncAccess>(serviceKey);
                    var ret = new OPFSInPlaceStream(fileManager, false);
                    ret.OnDisposed += async (_) => await InstanceDisposed(serviceKey);
                    WorkerStreams.Add(serviceKey, ret);
                    try
                    {
                        await ret.OpenPathInternal(root, path, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret.Dispose();
                        throw;
                    }
                    return ret;
                }
                else
                {
                    // unsupported scope
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimiter) _openLimiter.Release();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="fileMode"></param>
        /// <param name="fileAccess"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<OPFSInPlaceStream> OpenPath(string path, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            using var navigator = JS!.Get<Navigator>("navigator");
            using var root = await navigator.Storage.GetDirectory();
            return await OpenPath(root, path, fileMode, fileAccess, cancellationToken);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="root">Root directory handle</param>
        /// <param name="name">The file entry name to open</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<OPFSInPlaceStream> Open(FileSystemDirectoryHandle root, string name, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimiter = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimiter = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSAsyncSyncAccess();
                    OPFSInPlaceStream? ret = null;
                    try
                    {
                        ret = new OPFSInPlaceStream(fileManager, true);
                        await ret.OpenNameInternal(root, name, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret?.Dispose();
                        throw;
                    }
                    return ret;
                }
                else if (JS.IsWindow || JS.IsSharedWorkerGlobalScope)
                {
                    // this scope does not support sync access but can start a dedicated worker that does
                    // OPFSStreamWorkerService will run in the dedicated worker scope
                    _webWorker ??= await WebWorkerService.GetWebWorker();
                    var serviceKey = Guid.NewGuid().ToString();
                    await _webWorker!.New<IOPFSAsyncSyncAccess>(serviceKey, () => new OPFSAsyncSyncAccess());
                    var fileManager = _webWorker.GetKeyedService<IOPFSAsyncSyncAccess>(serviceKey);
                    var ret = new OPFSInPlaceStream(fileManager, false);
                    ret.OnDisposed += async (_) => await InstanceDisposed(serviceKey);
                    WorkerStreams.Add(serviceKey, ret);
                    try
                    {
                        await ret.OpenNameInternal(root, name, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret.Dispose();
                        throw;
                    }
                    return ret;
                }
                else
                {
                    // unsupported scope
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimiter) _openLimiter.Release();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="fileMode"></param>
        /// <param name="fileAccess"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<OPFSInPlaceStream> Open(string name, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            using var navigator = JS!.Get<Navigator>("navigator");
            using var root = await navigator.Storage.GetDirectory();
            return await Open(root, name, fileMode, fileAccess, cancellationToken);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileHandle"></param>
        /// <param name="fileMode"></param>
        /// <param name="fileAccess"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static async Task<OPFSInPlaceStream> Open(FileSystemFileHandle fileHandle, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimiter = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimiter = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSAsyncSyncAccess();
                    OPFSInPlaceStream? ret = null;
                    try
                    {
                        ret = new OPFSInPlaceStream(fileManager, true);
                        await ret.OpenInternal(fileHandle, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret?.Dispose();
                        throw;
                    }
                    return ret;
                }
                else if (JS.IsWindow || JS.IsSharedWorkerGlobalScope)
                {
                    // this scope does not support sync access but can start a dedicated worker that does
                    // OPFSStreamWorkerService will run in the dedicated worker scope
                    _webWorker ??= await WebWorkerService.GetWebWorker();
                    var serviceKey = Guid.NewGuid().ToString();
                    await _webWorker!.New<IOPFSAsyncSyncAccess>(serviceKey, () => new OPFSAsyncSyncAccess());
                    var fileManager = _webWorker.GetKeyedService<IOPFSAsyncSyncAccess>(serviceKey);
                    var ret = new OPFSInPlaceStream(fileManager, false);
                    ret.OnDisposed += async (_) => await InstanceDisposed(serviceKey);
                    WorkerStreams.Add(serviceKey, ret);
                    try
                    {
                        await ret.OpenInternal(fileHandle, fileMode, fileAccess, cancellationToken);
                    }
                    catch
                    {
                        ret.Dispose();
                        throw;
                    }
                    return ret;
                }
                else
                {
                    // unsupported scope
                    throw new NotImplementedException($"{nameof(OPFSInPlaceStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimiter) _openLimiter.Release();
            }
        }
        static async Task InstanceDisposed(string serviceKey)
        {
            try
            {
                // remove the file specific service
                var removed = WorkerStreams.Remove(serviceKey);
                if (removed && _webWorker != null)
                {
                    await _webWorker.RemoveKeyedService<IOPFSAsyncSyncAccess>(serviceKey);
                }
            }
            catch
            {
                // continue
            }
        }
        private SemaphoreSlim _handleLimiter = new SemaphoreSlim(1);
        private async Task WithHandle(Func<IOPFSAsyncSyncAccess, Task> withHandleFn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasHandle = false;
            try
            {
                await _handleLimiter.WaitAsync(cancellationToken);
                hasHandle = true;
                await withHandleFn(_handleManager).WaitAsync(cancellationToken);
            }
            finally
            {
                if (hasHandle) _handleLimiter.Release();
            }
        }
        private async Task<TResult> WithHandle<TResult>(Func<IOPFSAsyncSyncAccess, Task<TResult>> withHandleFn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasHandle = false;
            try
            {
                await _handleLimiter.WaitAsync(cancellationToken);
                hasHandle = true;
                return await withHandleFn(_handleManager).WaitAsync(cancellationToken);
            }
            finally
            {
                if (hasHandle) _handleLimiter.Release();
            }
        }
        private async Task OpenPathInternal(FileSystemDirectoryHandle root, string path, FileMode fileMode, FileAccess fileAccess, CancellationToken cancellationToken = default)
        {
            switch (fileAccess)
            {
                case FileAccess.Read:
                    _canRead = true;
                    _canWrite = false;
                    break;
                case FileAccess.Write:
                    _canRead = false;
                    _canWrite = true;
                    break;
                case FileAccess.ReadWrite:
                    _canRead = true;
                    _canWrite = true;
                    break;
            }
            _canWriteSync = _supportSyncAccess && _canWrite;
            _canReadSync = _supportSyncAccess && _canRead;
            var truncate = false;
            var seekToEnd = false;
            var fileHandle = await root.GetPathFileHandle(path, false);
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
                    seekToEnd = true;
                    break;
            }
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            _length = await WithHandle((h) => h.OpenAsync(fileHandle, truncate), cancellationToken);
            IsOpen = true;
            // if FileMode is append seek to the end of the file
            _position = seekToEnd ? _length : 0;
        }
        private async Task OpenNameInternal(FileSystemDirectoryHandle root, string name, FileMode fileMode, FileAccess fileAccess, CancellationToken cancellationToken = default)
        {
            switch (fileAccess)
            {
                case FileAccess.Read:
                    _canRead = true;
                    _canWrite = false;
                    break;
                case FileAccess.Write:
                    _canRead = false;
                    _canWrite = true;
                    break;
                case FileAccess.ReadWrite:
                    _canRead = true;
                    _canWrite = true;
                    break;
            }
            _canWriteSync = _supportSyncAccess && _canWrite;
            _canReadSync = _supportSyncAccess && _canRead;
            var truncate = false;
            var seekToEnd = false;
            FileSystemFileHandle? fileHandle = null;
            try
            {
                fileHandle = await root.GetFileHandle(name, false);
            }
            catch { }
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
                    seekToEnd = true;
                    break;
            }
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            _length = await WithHandle((h) => h.OpenAsync(fileHandle, truncate), cancellationToken);
            IsOpen = true;
            // if FileMode is append seek to the end of the file
            _position = seekToEnd ? _length : 0;
        }
        private async Task OpenInternal(FileSystemFileHandle fileHandle, FileMode fileMode, FileAccess fileAccess, CancellationToken cancellationToken = default)
        {
            if (fileHandle == null)
            {
                throw new FileNotFoundException();
            }
            switch (fileAccess)
            {
                case FileAccess.Read:
                    _canRead = true;
                    _canWrite = false;
                    break;
                case FileAccess.Write:
                    _canRead = false;
                    _canWrite = true;
                    break;
                case FileAccess.ReadWrite:
                    _canRead = true;
                    _canWrite = true;
                    break;
            }
            _canWriteSync = _supportSyncAccess && _canWrite;
            _canReadSync = _supportSyncAccess && _canRead;
            var truncate = false;
            var seekToEnd = false;
            switch (fileMode)
            {
                case FileMode.CreateNew:
                    // Creates a new file. An exception is raised if the file already exists.
                    if (fileHandle != null)
                    {
                        throw new NotSupportedException("File already exists");
                    }
                    throw new NotSupportedException("Cannot create file handle");
                case FileMode.Create:
                    // Creates a new file. If the file already exists, it is overwritten.
                    truncate = true;
                    break;
                case FileMode.Open:
                    // Opens an existing file. An exception is raised if the file does not exist.
                    break;
                case FileMode.OpenOrCreate:
                    // Opens the file if it exists. Otherwise, creates a new file.
                    break;
                case FileMode.Truncate:
                    // Opens an existing file. Once opened, the file is truncated so that its
                    // size is zero bytes. The calling process must open the file with at least
                    // WRITE access. An exception is raised if the file does not exist.
                    truncate = true;
                    break;
                case FileMode.Append:
                    // Opens the file if it exists and seeks to the end.  Otherwise,
                    // creates a new file.
                    seekToEnd = true;
                    break;
            }
            _length = await WithHandle((h) => h.OpenAsync(fileHandle, truncate), cancellationToken);
            IsOpen = true;
            // if FileMode is append seek to the end of the file
            _position = seekToEnd ? _length : 0;
        }
        /// <inheritdoc/>
        public override async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) return;
                await h.FlushAsync();
                _length = await h.GetSizeAsync();
            }, cancellationToken);
        }
        /// <summary>
        /// Returns true if this stream has been disposed
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (IsOpen)
            {
                try
                {
                    await WithHandle(async (h) =>
                    {
                        if (h == null) return;
                        await h.CloseAsync();
                    }, default);
                }
                catch { }
            }
            IsOpen = false;
            try
            {
                OnDisposed?.Invoke(this);
            }
            catch { }
        }
        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => await ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
        // ── Positional (offset-taking) access ──────────────────────────────────────────────────────────
        //
        // The offset comes from the CALLER, so nothing about this stream's cursor can affect where an
        // operation lands. FileSystemSyncAccessHandle is natively positional - read/write(buf, {at}), no
        // cursor of its own - so these map straight onto it. `Position` is a Stream-shaped abstraction
        // layered on top, and `Seek`-then-operate is TWO steps; WithHandle awaits a semaphore before the
        // operation runs, so a second caller on the SAME stream is guaranteed a suspension point between
        // them.
        //
        // MEASURED against the shipped package, before these overloads existed, by driving one stream from
        // several callers at once (OPFSInPlaceConcurrencyTests, real stream, one file):
        //   block 1 at offset 65536: byte 0 is 0x00, expected 0x02
        //   read of block 0 during a write to the last block: byte 0 is 0x00, expected 0xAA
        // Wrong bytes on disk, no exception raised.
        //
        // ⚠️ THAT IS NOT AN ARGUMENT FOR SHARING ONE STREAM. It was written as one, and TJ rejected the
        // design: "maybe 1 stream total per file is a bad idea. Stream is not shaped for it." A cursor is
        // per-reader state, so a shared Position has no coherent value to hold - the correct arrangement
        // is ONE STREAM PER CONSUMER. That costs nothing extra, because OPFSAsyncSyncAccess opens with
        // Mode = "readwrite-unsafe" and each stream therefore gets its OWN sync handle on the same file.
        //
        // These overloads earn their place anyway: a torrent store already addresses by absolute offset,
        // so it wants no seek and one round trip instead of two. Prefer them over Seek-then-operate for
        // any offset-addressed caller, and use them if you do share a stream - but do not share one.

        /// <summary>
        /// Reads <paramref name="count"/> bytes starting at <paramref name="offset"/> without touching
        /// <see cref="Position"/>. Safe to call concurrently with other positional reads and writes on the
        /// same stream.
        /// </summary>
        /// <param name="offset">Absolute byte offset to read from.</param>
        /// <param name="count">Number of bytes to read. Fewer are returned at end of file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The bytes read, as a <see cref="Uint8Array"/> the caller owns and must dispose.</returns>
        [return: WorkerTransfer]
        public async Task<Uint8Array> ReadUint8ArrayAtAsync(long offset, long count, CancellationToken cancellationToken = default)
        {
            return await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                return await h.ReadUint8ArrayAsync(offset, count);
            }, cancellationToken);
        }

        /// <summary>
        /// Writes <paramref name="data"/> at <paramref name="offset"/> without touching
        /// <see cref="Position"/>. Safe to call concurrently with other positional reads and writes on the
        /// same stream.
        /// </summary>
        /// <param name="data">The bytes to write. Stays JS-side; never crosses the managed heap.</param>
        /// <param name="offset">Absolute byte offset to write at.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes written.</returns>
        public async Task<long> WriteUint8ArrayAtAsync([WorkerTransfer] Uint8Array data, long offset, CancellationToken cancellationToken = default)
        {
            return await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                var bytesWritten = await h.WriteUint8ArrayAsync(data, offset);
                // Monotonic: a concurrent write to a lower offset must never shrink the recorded length.
                var end = offset + bytesWritten;
                if (end > _length) _length = end;
                return bytesWritten;
            }, cancellationToken);
        }

        // ── Cursor-based access ────────────────────────────────────────────────────────────────────────
        //
        // ⚠️ Every one of these captures Position SYNCHRONOUSLY, before awaiting anything, then delegates
        // to the positional API. That makes `Seek(x); await Op(...)` atomic with respect to another
        // caller's Seek, which the previous shape - reading _position inside the WithHandle lambda, after
        // the semaphore await - was not.
        //
        // ⚠️ It does NOT make the CURSOR itself shared-safe, and cannot: two callers advancing one
        // Position have no coherent answer. Concurrent callers must use the positional overloads above.
        // What is guaranteed here is that each operation acts on the offset its own Seek named.

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var at = _position;
            using var uint8ArrayData = await ReadUint8ArrayAtAsync(at, buffer.Length, cancellationToken);
            using var bufferHeapView = HeapView.Create<byte, Uint8Array>(buffer);
            bufferHeapView.View.Set(uint8ArrayData);
            _position = at + uint8ArrayData.ByteLength;
            if (_position > _length) _length = _position;
            return (int)uint8ArrayData.Length;
        }
        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var at = _position;
            using var uint8Array = HeapView.CreateCopy<byte, Uint8Array>(buffer);
            var bytesWritten = await WriteUint8ArrayAtAsync(uint8Array, at, cancellationToken);
            _position = at + bytesWritten;
            if (_position > _length) _length = _position;
        }
        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => await WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
        /// <inheritdoc/>
        public override async Task<Uint8Array> ReadUint8ArrayAsync(int count, CancellationToken cancellationToken = default)
        {
            var at = _position;
            var uint8Array = await ReadUint8ArrayAtAsync(at, count, cancellationToken);
            _position = at + uint8Array.ByteLength;
            return uint8Array;
        }
        /// <inheritdoc/>
        public override async Task WriteUint8ArrayAsync(Uint8Array data, CancellationToken cancellationToken = default)
        {
            var at = _position;
            var bytesWritten = await WriteUint8ArrayAtAsync(data, at, cancellationToken);
            _position = at + bytesWritten;
            if (_position > _length) _length = _position;
        }
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.End:
                    _position = Length + offset;
                    break;
            }
            return _position;
        }
        /// <summary>
        /// Set the stream length
        /// </summary>
        /// <param name="value">The new size</param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="Exception">Throws if the file is not open</exception>
        public async Task SetLengthAsync(long value, CancellationToken cancellationToken = default)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                await h.TruncateAsync(value);
                _length = value;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (_handleManager == null) throw new Exception("File not open");
            if (_syncAccessHandle == null)
            {
                Async.Run(async () =>
                {
                    try { await SetLengthAsync(value); } catch { }
                });
            }
            else
            {
                _syncAccessHandle.Truncate(value);
            }
        }
        void ThrowIfNotSyncSupoported()
        {
            if (!_supportSyncAccess) throw new NotImplementedException("Sync OPFSInPlaceStream access is only available in dedicated worker scopes");
            if (_syncAccessHandle == null) throw new NotImplementedException("SyncAccessHandle not available");
        }
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfNotSyncSupoported();
            using var heapView = HeapView.Create<byte, Uint8Array>(new ReadOnlyMemory<byte>(buffer, offset, count));
            var byteCount = _syncAccessHandle!.Write(heapView.View, new FileSystemSyncReadWriteOptions { At = _position });
            _position += byteCount;
            _length = _syncAccessHandle!.GetSize();
        }
        /// <inheritdoc/>
        public override void WriteUint8Array(Uint8Array data)
        {
            ThrowIfNotSyncSupoported();
            var byteCount = _syncAccessHandle!.Write(data, new FileSystemSyncReadWriteOptions { At = _position });
            _position += byteCount;
            _length = _syncAccessHandle!.GetSize();
        }
        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfNotSyncSupoported();
            _length = _syncAccessHandle!.GetSize();
            var bytesLeft = Math.Max(_length - _position, 0);
            var bytesToRead = Math.Max(0, Math.Min(bytesLeft, count));
            using var heapView = HeapView.Create<byte, Uint8Array>(new ReadOnlyMemory<byte>(buffer, offset, (int)bytesToRead));
            var byteCount = _syncAccessHandle!.Read(heapView.View, new FileSystemSyncReadWriteOptions { At = _position });
            _position += byteCount;
            return (int)byteCount;
        }
        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            ThrowIfNotSyncSupoported();
            _length = _syncAccessHandle!.GetSize();
            var count = buffer.Length;
            var bytesLeft = Math.Max(_length - _position, 0);
            var bytesToRead = Math.Max(0, Math.Min(bytesLeft, count));
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    var ptr = (IntPtr)p;
                    using var heapView = new HeapView<byte, Uint8Array>(ptr, bytesToRead);
                    var byteCount = _syncAccessHandle!.Read(heapView.View, new FileSystemSyncReadWriteOptions { At = _position });
                    _position += byteCount;
                    return (int)byteCount;
                }
            }
        }
        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfNotSyncSupoported();
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    var ptr = (IntPtr)p;
                    using var heapView = new HeapView<byte, Uint8Array>(ptr, buffer.Length);
                    var byteCount = _syncAccessHandle!.Write(heapView.View, new FileSystemSyncReadWriteOptions { At = _position });
                    _position += byteCount;
                    _length = _syncAccessHandle!.GetSize();
                }
            }
        }
        /// <inheritdoc/>
        public override Uint8Array ReadUint8Array(int count)
        {
            ThrowIfNotSyncSupoported();
            _length = _syncAccessHandle!.GetSize();
            var bytesLeft = Math.Max(_length - _position, 0);
            var bytesToRead = Math.Max(0, Math.Min(bytesLeft, count));
            var uint8Array = new Uint8Array(bytesToRead);
            var byteCount = _syncAccessHandle!.Read(uint8Array, new FileSystemSyncReadWriteOptions { At = _position });
            _position += byteCount;
            return uint8Array;
        }
        /// <inheritdoc/>
        public override void Flush()
        {
            ThrowIfNotSyncSupoported();
            _syncAccessHandle!.Flush();
        }
        /// <inheritdoc/>
        public override void Close()
        {
            _syncAccessHandle?.Close();
        }
    }
}
