using System;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// One operator/engine answer for one check, at one point in the run. Answers are appended to
    /// PreflightRun.History, never overwritten in place, so a report can show the final answer plus
    /// whether it changed during the run (e.g. flipped after navigating Back).
    /// </summary>
    public class CheckResultRecord
    {
        public string CheckId { get; set; }
        public CheckOutcome Outcome { get; set; }
        public string Notes { get; set; }
        public string Detail { get; set; }
        public DateTime TimestampUtc { get; set; }

        /// <summary>Snapshotted from the definition at record time, so a report stays self-contained
        /// even if the JSON checklist changes after the run.</summary>
        public string CheckTitle { get; set; }
        public CheckSeverity Severity { get; set; }
    }
}
