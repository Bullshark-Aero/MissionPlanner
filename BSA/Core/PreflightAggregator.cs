using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Pure aggregation: per-check outcomes + severities -> run-level PreflightResult. Matches WP1's
    /// aggregation table exactly: Fail/Unknown at Critical severity -> NoGo; at Warning -> Warning; at
    /// Info -> recorded only (no gate impact). NotApplicable is always recorded only, never blocks Go by
    /// itself. Pass is neutral.
    ///
    /// signOffCompleted=false always yields Unknown unless a critical blocker already makes it NoGo -
    /// this lets PreflightRunEngine.CompleteRun() call this with signOffCompleted=true to get the real
    /// final verdict. It is NOT used to decide an aborted run's result: Abort() sets PreflightResult.
    /// Unknown directly and unconditionally (an aborted run is incomplete, not "failed", even if a
    /// critical check had already failed before the abort - see the plan's Aggregation section).
    /// </summary>
    public static class PreflightAggregator
    {
        public static PreflightResult Aggregate(IEnumerable<CheckResultRecord> latestPerCheck, bool signOffCompleted)
        {
            var records = latestPerCheck?.ToList() ?? new List<CheckResultRecord>();

            var hasCriticalBlocker = records.Any(r => r.Severity == CheckSeverity.Critical && IsBlocking(r.Outcome));
            if (hasCriticalBlocker)
                return PreflightResult.NoGo;

            if (!signOffCompleted)
                return PreflightResult.Unknown;

            var hasWarningBlocker = records.Any(r => r.Severity == CheckSeverity.Warning && IsBlocking(r.Outcome));
            return hasWarningBlocker ? PreflightResult.Warning : PreflightResult.Go;
        }

        static bool IsBlocking(CheckOutcome outcome) => outcome == CheckOutcome.Fail || outcome == CheckOutcome.Unknown;
    }
}
