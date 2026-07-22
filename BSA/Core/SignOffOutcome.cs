using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Core
{
    /// <summary>One Auto check whose re-verified value at the Sign Off click differed from what its
    /// page was displaying - see PreflightRunEngine.TryCompleteRun and the plan's §4a "commit on the
    /// state you were shown" rule.</summary>
    public sealed class AutoReverifyChange
    {
        public string CheckId { get; }
        public CheckOutcome Before { get; }
        public CheckOutcome After { get; }
        public string Detail { get; }

        public AutoReverifyChange(string checkId, CheckOutcome before, CheckOutcome after, string detail)
        {
            CheckId = checkId;
            Before = before;
            After = after;
            Detail = detail;
        }
    }

    /// <summary>Result of PreflightRunEngine.TryCompleteRun - a non-throwing signal for the two
    /// recoverable refusal cases (unanswered checks the jump rail let the operator skip past; an
    /// Auto check that moved since it was last shown), so the sign-off UI can react instead of
    /// catching an exception on every deliberate "not ready yet" state.</summary>
    public sealed class SignOffOutcome
    {
        public bool Completed { get; }
        public IReadOnlyList<string> UnansweredCheckIds { get; }
        public IReadOnlyList<AutoReverifyChange> ChangedAutoChecks { get; }

        public SignOffOutcome(bool completed, IReadOnlyList<string> unansweredCheckIds,
            IReadOnlyList<AutoReverifyChange> changedAutoChecks)
        {
            Completed = completed;
            UnansweredCheckIds = unansweredCheckIds ?? Array.Empty<string>();
            ChangedAutoChecks = changedAutoChecks ?? Array.Empty<AutoReverifyChange>();
        }

        public static readonly SignOffOutcome AlreadyCompleted =
            new SignOffOutcome(true, Array.Empty<string>(), Array.Empty<AutoReverifyChange>());
    }
}
