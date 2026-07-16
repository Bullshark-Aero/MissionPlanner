using System;

namespace MissionPlanner.BSA.Lock
{
    public class LockStatusChangedEventArgs : EventArgs
    {
        public LockState State { get; }
        public string Reason { get; }

        public LockStatusChangedEventArgs(LockState state, string reason)
        {
            State = state;
            Reason = reason;
        }
    }
}
