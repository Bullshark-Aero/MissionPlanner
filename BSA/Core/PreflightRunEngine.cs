using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Checks;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Orchestrates one preflight run: step navigation, auto-evaluation, operator answers, abort, and
    /// final sign-off. A plain newable class (not a singleton) so it's unit-testable in isolation -
    /// BsaPreflightService owns one instance per run attempt.
    ///
    /// State machine: InProgress(stepIndex) -&gt; AwaitingSignOff -&gt; Completed(result). Aborted is
    /// reachable from either of the first two states. Previous() can step back out of AwaitingSignOff
    /// into InProgress so the sign-off screen is reviewable, not a one-way door.
    /// </summary>
    public class PreflightRunEngine
    {
        readonly AutoCheckEvaluator _autoEvaluator;
        readonly RegisteredCheckRegistry _registry;

        public PreflightRun Run { get; }

        public PreflightRunEngine(IList<PreflightCheckDefinition> checks, AutoCheckEvaluator autoEvaluator,
            RegisteredCheckRegistry registry, string operatorName, string missionBaselineHash = null)
        {
            if (checks == null || checks.Count == 0)
                throw new ArgumentException("A preflight run needs at least one check.", nameof(checks));

            _autoEvaluator = autoEvaluator ?? throw new ArgumentNullException(nameof(autoEvaluator));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            Run = new PreflightRun
            {
                Checks = checks.ToList(),
                OperatorName = operatorName,
                StartedUtc = DateTime.UtcNow,
                State = PreflightRunState.InProgress,
                MissionBaselineHash = missionBaselineHash
            };
        }

        public PreflightCheckDefinition CurrentCheck =>
            Run.State == PreflightRunState.InProgress && Run.CurrentStepIndex < Run.Checks.Count
                ? Run.Checks[Run.CurrentStepIndex]
                : null;

        /// <summary>
        /// Auto-evaluates the current check (generic evaluator or registered check, depending on shape)
        /// and returns it as a suggestion - does not record anything. Manual checks have nothing to
        /// evaluate and return Unknown; callers should not call this for Manual steps.
        /// </summary>
        public (CheckOutcome outcome, string detail) EvaluateCurrentCheck()
        {
            var check = CurrentCheck;
            if (check == null || check.Type == CheckType.Manual)
                return (CheckOutcome.Unknown, null);

            if (!string.IsNullOrWhiteSpace(check.Check))
            {
                if (_registry.TryGet(check.Check, out var registered))
                    return (registered.Evaluate(check, out var detail), detail);

                return (CheckOutcome.Unknown, $"Registered check '{check.Check}' not found.");
            }

            return _autoEvaluator.Evaluate(check);
        }

        /// <summary>
        /// Records the current check's answer (operator-chosen for Manual/Semi, engine-evaluated for
        /// Auto) and appends it to history. Does not advance the step - call Next() separately.
        /// </summary>
        public void RecordResult(CheckOutcome outcome, string notes = null, string detail = null)
        {
            RequireInProgress();
            var check = CurrentCheck ?? throw new InvalidOperationException("No current check to record a result for.");

            if (outcome == CheckOutcome.NotApplicable && !check.AllowNotApplicable)
                throw new InvalidOperationException($"Check '{check.Id}' does not allow N/A.");

            if (outcome == CheckOutcome.Fail && check.RequiresNoteOnFail && string.IsNullOrWhiteSpace(notes))
                throw new InvalidOperationException($"Check '{check.Id}' requires a note when failed.");

            Run.History.Add(new CheckResultRecord
            {
                CheckId = check.Id,
                CheckTitle = check.Title,
                Severity = check.Severity ?? CheckSeverity.Critical,
                Outcome = outcome,
                Notes = notes,
                Detail = detail,
                TimestampUtc = DateTime.UtcNow
            });
        }

        public bool CanGoNext => Run.State == PreflightRunState.InProgress;

        public bool CanGoPrevious =>
            Run.State == PreflightRunState.AwaitingSignOff ||
            (Run.State == PreflightRunState.InProgress && Run.CurrentStepIndex > 0);

        /// <summary>Advances past the current step. The current step must already have a recorded
        /// answer - this is a structural invariant, not just a UI nicety, so an operator can't
        /// click through a check unanswered.</summary>
        public void Next()
        {
            RequireInProgress();
            var current = CurrentCheck;
            if (current != null && !Run.History.Any(r => r.CheckId == current.Id))
                throw new InvalidOperationException($"Check '{current.Id}' has not been answered yet.");

            if (Run.CurrentStepIndex >= Run.Checks.Count - 1)
            {
                Run.State = PreflightRunState.AwaitingSignOff;
                return;
            }

            Run.CurrentStepIndex++;
        }

        /// <summary>Steps back. From AwaitingSignOff this re-opens the last step for review/edit rather
        /// than moving an index - the sign-off screen is reviewable, not a one-way door.</summary>
        public void Previous()
        {
            if (Run.State != PreflightRunState.InProgress && Run.State != PreflightRunState.AwaitingSignOff)
                throw new InvalidOperationException($"Run is not active (state: {Run.State}).");

            if (Run.State == PreflightRunState.AwaitingSignOff)
            {
                Run.State = PreflightRunState.InProgress;
                return;
            }

            if (Run.CurrentStepIndex > 0)
                Run.CurrentStepIndex--;
        }

        /// <summary>
        /// Idempotent - a second Abort() call (e.g. the wizard's FormClosing firing after an explicit
        /// Abort button was already handled) is a no-op, not an error or a double-written report. Always
        /// resolves to PreflightResult.Unknown regardless of any check results recorded so far - an
        /// aborted run is incomplete, not "failed" (see PreflightAggregator's doc comment).
        /// </summary>
        public void Abort(string reason)
        {
            if (Run.State == PreflightRunState.Completed || Run.State == PreflightRunState.Aborted)
                return;

            Run.State = PreflightRunState.Aborted;
            Run.Result = PreflightResult.Unknown;
            Run.AbortReason = reason;
            Run.EndedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Idempotent final sign-off. Only valid from AwaitingSignOff (reached by calling Next() past
        /// the last check, which itself guarantees every check was answered - see Next()).
        /// </summary>
        public void CompleteRun()
        {
            if (Run.State == PreflightRunState.Completed || Run.State == PreflightRunState.Aborted)
                return;

            if (Run.State != PreflightRunState.AwaitingSignOff)
                throw new InvalidOperationException($"Cannot complete from state {Run.State}; step through every check first.");

            Run.Result = PreflightAggregator.Aggregate(Run.LatestPerCheck, signOffCompleted: true);
            Run.State = PreflightRunState.Completed;
            Run.EndedUtc = DateTime.UtcNow;
        }

        void RequireInProgress()
        {
            if (Run.State != PreflightRunState.InProgress)
                throw new InvalidOperationException($"Run is not in progress (state: {Run.State}).");
        }
    }
}
