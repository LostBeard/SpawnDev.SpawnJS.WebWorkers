using Microsoft.JSInterop;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// An ExtendableEvent that was initially missed while Blazor was loading, but was held using waitUntil() so that Blazor can handle it.<br />
    /// </summary>
    internal class MissedExtendableEvent : ExtendableEvent, IMissedEvent
    {
        ///<inheritdoc/>
        public MissedExtendableEvent(SpawnJSObjectReference _ref) : base(_ref) { }
        ///<inheritdoc/>
        public void WaitResolve() => JSRef!.CallVoid("waitResolve");
        ///<inheritdoc/>
        public void WaitReject() => JSRef!.CallVoid("waitReject");
        ///<inheritdoc/>
        public bool IsExtended => !JSRef!.IsUndefined("waitResolve");
    }
}

