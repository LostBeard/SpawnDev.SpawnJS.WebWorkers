using Microsoft.JSInterop;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// An Event that was initially missed while Blazor was loading, but was held using waitUntil() so that Blazor can handle it.<br />
    /// </summary>
    internal class MissedContentIndexEvent : ContentIndexEvent, IMissedEvent
    {
        ///<inheritdoc/>
        public MissedContentIndexEvent(SpawnJSObjectReference _ref) : base(_ref) { }
        ///<inheritdoc/>
        public void WaitResolve() => JSRef!.CallVoid("waitResolve");
        ///<inheritdoc/>
        public void WaitReject() => JSRef!.CallVoid("waitReject");
        ///<inheritdoc/>
        public bool IsExtended => !JSRef!.IsUndefined("waitResolve");
    }
}

