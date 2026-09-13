using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS.Worker
{

    public interface IOPFSAsyncSyncAccess
    {
        FileSystemSyncAccessHandle? SyncAccessHandle { get; }

        Task<long> OpenAsync(string path, FileMode fileMode);

        Task<long> OpenAsync(FileSystemFileHandle draftHandle, bool truncate);

        Task FlushAsync();

        Task TruncateAsync(long newSize);

        Task<long> GetSizeAsync();

        Task<long> WriteUint8ArrayAsync([WorkerTransfer] Uint8Array srcBuffer, long destOffset = 0);

        [return: WorkerTransfer]
        Task<Uint8Array> ReadUint8ArrayAsync(long srcOffset = 0);

        [return: WorkerTransfer]
        Task<Uint8Array> ReadUint8ArrayAsync(long srcOffset, long count);

        Task<bool> CloseAsync();
    }
}
