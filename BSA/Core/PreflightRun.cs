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

        /// <summary>Fixed at construction, never reassignable or reorderable - PreflightReportWriter
        /// hashes this list (BsaHash.HashObject preserves array order) into every report's
        /// PreflightConfigHash, so a display-order view (see PreflightPagePlan) must never be written
        /// back here. IReadOnlyList makes that a compile error rather than a hoped-for convention.</summary>
        public IReadOnlyList<PreflightCheckDefinition> Checks { get; }

        /// <summary>Index into PreflightRunEngine.Pages, not into Checks - a page can hold several
        /// checks (see PreflightPagePlan). Named CurrentPageIndex (not CurrentStepIndex) to make that
        /// distinction unmissable at every call site.</summary>
        public int CurrentPageIndex { get; set; }

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

        /// <summary>Scoped to operator-authored answers only - re-evaluating an Auto check (page
        /// revisit, sign-off re-verification) must never itself count as "the operator changed their
        /// mind". See CheckResultSource and HasAutoReverifyChange for the Auto-specific signal.</summary>
        public bool HasChangedAnswer(string checkId) =>
            History.Count(r => r.CheckId == checkId && r.Source == CheckResultSource.Operator) > 1;

        /// <summary>True if an Auto check's most recent re-verification (at AwaitingSignOff entry or
        /// the Sign Off click - PreflightRunEngine.TryCompleteRun) disagrees with the value first
        /// shown when its page was displayed. This is the signal a mid-run mission edit (or any other
        /// drifted Auto value) produces - see WP1_wizard_grouping_pagination_plan.md §4.</summary>
        public bool HasAutoReverifyChange(string checkId)
        {
            var initial = History.FirstOrDefault(r => r.CheckId == checkId && r.Source == CheckResultSource.AutoInitial);
            if (initial == null) return false;

            var latestReverify = History.LastOrDefault(r => r.CheckId == checkId && r.Source == CheckResultSource.AutoReverify);
            return latestReverify != null && latestReverify.Outcome != initial.Outcome;
        }

        public PreflightRun(IReadOnlyList<PreflightCheckDefinition> checks)
        {
            Checks = checks ?? throw new ArgumentNullException(nameof(checks));
        }
    }
}
