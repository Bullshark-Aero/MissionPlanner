using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Core
{
    public enum PreflightRunState
    {
        InProgress,
        AwaitingSignOff,
        Completed,
        Aborted
    }

    /// <summary>
    /// Mutable state for one preflight attempt, owned by a single PreflightRunEngine instance.
    /// </summary>
    public class PreflightRun
    {
        public string RunId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public DateTime StartedUtc { get; set; }
        public DateTime? EndedUtc { get; set; }

        public PreflightRunState State { get; set; }
        public PreflightResult Result { get; set; } = PreflightResult.Unknown;
        public string AbortReason { get; set; }

        public List<PreflightCheckDefinition> Checks { get; set; } = new List<PreflightCheckDefinition>();
        public int CurrentStepIndex { get; set; }

        /// <summary>Append-only answer history - a given check id may appear more than once.</summary>
        public List<CheckResultRecord> History { get; } = new List<CheckResultRecord>();

        public string OperatorName { get; set; }

        /// <summary>Mission hash captured once at run start (by whoever constructs the
        /// PreflightRunEngine), compared later by the "mission unchanged during preflight" check.
        /// Null if no mission was loaded at run start.</summary>
        public string MissionBaselineHash { get; set; }

        /// <summary>Most recent answer for each check id, in first-answered order. What reports and
        /// aggregation actually look at.</summary>
        public IEnumerable<CheckResultRecord> LatestPerCheck =>
            History.GroupBy(r => r.CheckId).Select(g => g.Last());

        public bool HasChangedAnswer(string checkId) => History.Count(r => r.CheckId == checkId) > 1;
    }
}
