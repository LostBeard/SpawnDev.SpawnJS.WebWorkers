using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers.OPFS
{

    public interface IOPFSStreamWorkerService
    {

        Task<long> Open(string path, FileMode fileMode);

        Task<long> Open(FileSystemFileHandle draftHandle, bool truncate);

        Task Flush();

        Task Truncate(long newSize);

        Task<long> GetSize();

        Task<long> WriteUint8Array([WorkerTransfer] Uint8Array srcBuffer, long destOffset = 0);

        [return: WorkerTransfer]
        Task<Uint8Array> ReadUint8Array(long srcOffset = 0);

        [return: WorkerTransfer]
        Task<Uint8Array> ReadUint8Array(long srcOffset, long count);

        Task<bool> Close();
    }
}
