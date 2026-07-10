using System;

namespace MissionPlanner.BSA.Core
{
    public class PreflightStatusChangedEventArgs : EventArgs
    {
        public PreflightResult Result { get; }
        public PreflightRun Run { get; }

        public PreflightStatusChangedEventArgs(PreflightResult result, PreflightRun run)
        {
            Result = result;
            Run = run;
        }
    }
}
