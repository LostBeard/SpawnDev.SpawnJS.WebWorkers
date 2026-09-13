using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS
{
    /// <summary>
    /// Get persistent file streams
    /// </summary>
    public static class SyncWorkerOPFS
    {
        /// <summary>
        /// If true and OPFSStreamSupported, a dedicated worker will be used for sync access
        /// </summary>
        public static bool OPFSStreamEnabledIfAvailable { get; set; } = true;
        /// <summary>
        /// If true, OPFSStream will be used for OPFS streams
        /// </summary>
        public static bool OPFSStreamEnabled => OPFSStreamEnabledIfAvailable && OPFSStreamSupported;
        /// <summary>
        /// Returns true if OPFSStream.Supported
        /// </summary>
        public static bool OPFSStreamSupported => OPFSInPlaceStream.Supported;
        static SpawnJSRuntime JS => SpawnJSRuntime.Instance;
        static StorageManager? _storage;
        static FileSystemDirectoryHandle? _root;
        static Task? _ready = null;
        static Task Ready => _ready ??= InitAsync();
        static async Task InitAsync()
        {
            if (OperatingSystem.IsBrowser())
            {
                using var navigator = JS!.Get<Navigator>("navigator");
                _storage = navigator?.Storage;
                if (_storage != null)
                {
                    _root = await _storage.GetDirectory();
                }
            }
        }
        public static async Task<Stream> OpenInPlacePathStream(this FileSystemDirectoryHandle root, string path, FileMode fileMode = FileMode.Open, FileAccess fileAccess = FileAccess.Read, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException();
            if (OPFSStreamEnabled)
            {
                return await OPFSInPlaceStream.OpenPath(root, path, fileMode, fileAccess, cancellationToken);
            }
            else
            {
                return await root.OpenPathStream(path, fileMode, fileAccess, OPFSSyncMode.Auto, cancellationToken);
            }
        }
        public static async Task<Stream> OpenInPlacePathStream(string path, FileMode fileMode = FileMode.Open, FileAccess fileAccess = FileAccess.Read, CancellationToken cancellationToken = default)
        {
            if (OperatingSystem.IsBrowser())
            {
                await Ready;
                if (_root == null) throw new PlatformNotSupportedException();
                return await OpenInPlacePathStream(_root, path, fileMode, fileAccess, cancellationToken);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }
        public static async Task<Stream> OpenInPlaceStream(this FileSystemDirectoryHandle root, string name, FileMode fileMode = FileMode.Open, FileAccess fileAccess = FileAccess.Read, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException();
            if (OPFSStreamEnabled)
            {
                return await OPFSInPlaceStream.Open(root, name, fileMode, fileAccess, cancellationToken);
            }
            else
            {
                return await root.OpenStream(name, fileMode, fileAccess, OPFSSyncMode.Auto, cancellationToken);
            }
        }
        public static async Task<Stream> OpenInPlaceStream(string name, FileMode fileMode = FileMode.Open, FileAccess fileAccess = FileAccess.Read, CancellationToken cancellationToken = default)
        {
            if (OperatingSystem.IsBrowser())
            {
                await Ready;
                if (_root == null) throw new PlatformNotSupportedException();
                return await OpenInPlaceStream(_root, name, fileMode, fileAccess, cancellationToken);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }
        public static async Task<Stream> OpenInPlaceStream(this FileSystemFileHandle fileHandle, FileMode fileMode = FileMode.Open, FileAccess fileAccess = FileAccess.Read, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException();
            if (OPFSStreamEnabled)
            {
                return await OPFSInPlaceStream.Open(fileHandle, fileMode, fileAccess, cancellationToken);
            }
            else
            {
                return await fileHandle.OpenStream(fileMode, fileAccess, OPFSSyncMode.Auto, cancellationToken);
            }
        }
    }
}
