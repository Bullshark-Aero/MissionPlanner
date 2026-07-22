using System;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Where a CheckResultRecord came from. Operator (the default) is any answer a human gave.
    /// AutoInitial/AutoReverify are both engine-recorded Auto-check evaluations, distinguished so
    /// PreflightRun.HasChangedAnswer can stay scoped to "the operator changed their mind" (its
    /// original meaning) instead of also tripping every time an Auto check is simply re-evaluated -
    /// see WP1_wizard_grouping_pagination_plan.md §4.
    /// </summary>
    public enum CheckResultSource
    {
        Operator,
        /// <summary>Recorded when an Auto check's page is first shown.</summary>
        AutoInitial,
        /// <summary>Recorded when re-evaluating Auto checks at the AwaitingSignOff transition and
        /// again on the Sign Off click itself (PreflightRunEngine.TryCompleteRun).</summary>
        AutoReverify
    }

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
        public CheckResultSource Source { get; set; } = CheckResultSource.Operator;

        /// <summary>Snapshotted from the definition at record time, so a report stays self-contained
        /// even if the JSON checklist changes after the run.</summary>
        public string CheckTitle { get; set; }
        public CheckSeverity Severity { get; set; }

        /// <summary>Snapshotted Group (see PreflightCheckDefinition.Group), same rationale as
        /// CheckTitle/Severity. Null for an ungrouped checklist.</summary>
        public string Group { get; set; }
    }
}
