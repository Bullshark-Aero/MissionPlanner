using System;

namespace MissionPlanner
{
    /// <summary>
    /// Extension point for MAVLinkInterface.setParamAsync so the BSA Operational Lock (WP3, lives in
    /// the MissionPlanner.exe project) can gate parameter writes without this lower-level project
    /// (MissionPlanner.ArduPilot.csproj) referencing it - MissionPlanner.csproj already references
    /// this project, so a direct reference the other way would be circular. Same decoupling idiom as
    /// System.CustomMessageBox's static ShowEvent: this class knows nothing about BSA, it just exposes
    /// a hook that the exe wires up at startup (BsaLockComposition).
    ///
    /// ParamWriteCheck must never block the calling thread or call back into UI: setParamAsync is
    /// reached synchronously from UI-thread event handlers via the sync setParam(...).AwaitSync()
    /// wrapper, which is a raw blocking wait with no message pump (ExtLibs/Utilities/Extensions.cs) -
    /// any Invoke/BeginInvoke-and-wait back onto that thread from here would deadlock.
    /// </summary>
    public static class BsaLockGate
    {
        /// <summary>Returns null to allow the write, or a refusal message to block it. Left unset
        /// (null) when no BSA lock layer is wired up - setParamAsync must treat that as "always allow,"
        /// not "always refuse."</summary>
        public static Func<string, double, string> ParamWriteCheck;
    }
}
