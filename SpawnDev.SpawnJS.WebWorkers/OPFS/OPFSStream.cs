using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS
{
    /// <summary>
    /// Access OPFS asynchronously using the synchronous API from Window, DedicatedWorker, and SharedWorker scopes.<br/>
    /// If not already running in a dedicated worker and the current scope can start a dedicateed worker<br/>
    /// OPFSStream.Open() will start a new dedicated worker is one is not already working and use it for OPFS access.<br/>
    /// Purpose:<br/>
    /// This allows in-place read and write access to OPFS files which is not possible with the more easily accessible async access.
    /// </summary>
    public class OPFSStream : JSReadWriteStreamBase
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
        private IOPFSStreamWorkerService _handleManager;
        /// <summary>
        /// Fires when this stream is disposed
        /// </summary>
        public event Action<OPFSStream>? OnDisposed;
        public static Dictionary<string, OPFSStream> WorkerStreams { get; } = new Dictionary<string, OPFSStream>();
        private OPFSStream(IOPFSStreamWorkerService handleManager)
        {
            _handleManager = handleManager;
        }
        private static SemaphoreSlim _openLimiter = new SemaphoreSlim(1);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="root">Root directory handle</param>
        /// <param name="path">The file entry name to open</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static async Task<OPFSStream> OpenPath(FileSystemDirectoryHandle root, string path, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimtier = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimtier = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSStreamWorkerService();
                    OPFSStream? ret = null;
                    try
                    {
                        ret = new OPFSStream(fileManager);
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
                    await _webWorker!.New<IOPFSStreamWorkerService>(serviceKey, () => new OPFSStreamWorkerService());
                    var fileManager = _webWorker.GetKeyedService<IOPFSStreamWorkerService>(serviceKey);
                    var ret = new OPFSStream(fileManager);
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
                    throw new NotImplementedException($"{nameof(OPFSStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimtier) _openLimiter.Release();
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
        public static async Task<OPFSStream> OpenPath(string path, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
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
        public static async Task<OPFSStream> Open(FileSystemDirectoryHandle root, string name, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimtier = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimtier = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSStreamWorkerService();
                    OPFSStream? ret = null;
                    try
                    {
                        ret = new OPFSStream(fileManager);
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
                    await _webWorker!.New<IOPFSStreamWorkerService>(serviceKey, () => new OPFSStreamWorkerService());
                    var fileManager = _webWorker.GetKeyedService<IOPFSStreamWorkerService>(serviceKey);
                    var ret = new OPFSStream(fileManager);
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
                    throw new NotImplementedException($"{nameof(OPFSStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimtier) _openLimiter.Release();
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
        public static async Task<OPFSStream> Open(string name, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
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
        public static async Task<OPFSStream> Open(FileSystemFileHandle fileHandle, FileMode fileMode = FileMode.OpenOrCreate, FileAccess fileAccess = FileAccess.ReadWrite, CancellationToken cancellationToken = default)
        {
            var haveLimtier = false;
            try
            {
                await _openLimiter.WaitAsync(cancellationToken);
                haveLimtier = true;
                if (WebWorkerService == null)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires WebWorkerService");
                }
                else if (JS == null || !JS.IsBrowser)
                {
                    throw new NotImplementedException($"{nameof(OPFSStream)} requires OperatingSystem.IsBrowser() == true");
                }
                else if (JS.IsServiceWorkerGlobalScope)
                {
                    // this scope does not support sync access and cannot start a dedicated worker that does
                    throw new NotImplementedException($"{nameof(OPFSStream)} is not supported in service workers. ServiceWorker scope does not support sync access and cannot start a dedicated worker that does.");
                }
                else if (JS.IsDedicatedWorkerGlobalScope)
                {
                    // this scope supports sync access
                    // OPFSStreamWorkerService will run in this scope
                    var fileManager = new OPFSStreamWorkerService();
                    OPFSStream? ret = null;
                    try
                    {
                        ret = new OPFSStream(fileManager);
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
                    await _webWorker!.New<IOPFSStreamWorkerService>(serviceKey, () => new OPFSStreamWorkerService());
                    var fileManager = _webWorker.GetKeyedService<IOPFSStreamWorkerService>(serviceKey);
                    var ret = new OPFSStream(fileManager);
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
                    throw new NotImplementedException($"{nameof(OPFSStream)} unsupported scope {JS.GlobalScope}");
                }
            }
            finally
            {
                if (haveLimtier) _openLimiter.Release();
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
                    await _webWorker.RemoveKeyedService<IOPFSStreamWorkerService>(serviceKey);
                }
            }
            catch
            {
                // continue
            }
        }
        private SemaphoreSlim _handleLimiter = new SemaphoreSlim(1);
        private async Task WithHandle(Func<IOPFSStreamWorkerService, Task> withHandleFn, CancellationToken cancellationToken)
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
        private async Task<TResult> WithHandle<TResult>(Func<IOPFSStreamWorkerService, Task<TResult>> withHandleFn, CancellationToken cancellationToken)
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
            _length = await WithHandle((h) => h.Open(fileHandle, truncate), cancellationToken);
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
            var truncate = false;
            var seekToEnd = false;
            var fileHandle = await root.GetFileHandle(name, false);
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
            _length = await WithHandle((h) => h.Open(fileHandle, truncate), cancellationToken);
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
            _length = await WithHandle((h) => h.Open(fileHandle, truncate), cancellationToken);
            // if FileMode is append seek to the end of the file
            _position = seekToEnd ? _length : 0;
        }
        /// <inheritdoc/>
        public override async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) return;
                await h.Flush();
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
            try
            {
                await WithHandle(async (h) =>
                {
                    if (h == null) return;
                    await h.Close();
                }, default);
            }
            catch { }
            try
            {
                OnDisposed?.Invoke(this);
            }
            catch { }
        }
        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                using var uint8ArrayData = await h.ReadUint8Array(_position, count);
                using var bufferHeapView = HeapView.Create<byte, Uint8Array>(new ReadOnlyMemory<byte>(buffer, offset, count));
                bufferHeapView.View.Set(uint8ArrayData);
                _position = _position + uint8ArrayData.ByteLength;
                if (_position > _length) _length = _position;
                return (int)uint8ArrayData.Length;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                using var uint8ArrayData = await h.ReadUint8Array(_position, buffer.Length);
                using var bufferHeapView = HeapView.Create<byte, Uint8Array>(buffer);
                bufferHeapView.View.Set(uint8ArrayData);
                _position = _position + uint8ArrayData.ByteLength;
                if (_position > _length) _length = _position;
                return (int)uint8ArrayData.Length;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                using var uint8Array = HeapView.CreateCopy<byte, Uint8Array>(buffer);
                var bytesWritten = await h.WriteUint8Array(uint8Array, _position);
                _position = _position + bytesWritten;
                if (_position > _length) _length = _position;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                using var uint8Array = HeapView.CreateCopy<byte, Uint8Array>(new ReadOnlyMemory<byte>(buffer, offset, count));
                var bytesWritten = await h.WriteUint8Array(uint8Array, _position);
                _position = _position + bytesWritten;
                if (_position > _length) _length = _position;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override async Task<Uint8Array> ReadUint8ArrayAsync(int count, CancellationToken cancellationToken = default)
        {
            return await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                var uint8Array = await h.ReadUint8Array(_position, count);
                var bytesRead = uint8Array.ByteLength;
                _position += bytesRead;
                return uint8Array;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override async Task WriteUint8ArrayAsync(Uint8Array data, CancellationToken cancellationToken = default)
        {
            await WithHandle(async (h) =>
            {
                if (h == null) throw new Exception("File not open");
                var bytesWritten = await h.WriteUint8Array(data, _position);
                _position = _position + bytesWritten;
                if (_position > _length) _length = _position;
            }, cancellationToken);
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
                await h.Truncate(value);
                _length = value;
            }, cancellationToken);
        }
        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (_handleManager == null) throw new Exception("File not open");
            Async.Run(async () =>
            {
                try { await SetLengthAsync(value); } catch { }
            });
        }
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        /// <inheritdoc/>
        public override void WriteUint8Array(Uint8Array data) => throw new NotImplementedException();
        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        /// <inheritdoc/>
        public override Uint8Array ReadUint8Array(int count) => throw new NotImplementedException();
        /// <inheritdoc/>
        public override void Flush() => throw new NotImplementedException();
    }
}
