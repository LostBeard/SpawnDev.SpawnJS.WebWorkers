
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    internal class MissedSyncEvent : SyncEvent, IMissedEvent
    {
        ///<inheritdoc/>
        public MissedSyncEvent(SpawnJSObjectReference _ref) : base(_ref) { }
        ///<inheritdoc/>
        public void WaitResolve() => JSRef!.CallVoid("waitResolve");
        ///<inheritdoc/>
        public void WaitReject() => JSRef!.CallVoid("waitReject");
        ///<inheritdoc/>
        public bool IsExtended => JSRef!.Has("waitResolve");
    }
}

