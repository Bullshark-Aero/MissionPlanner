using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Checks;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Orchestrates one preflight run: page navigation (PreflightPagePlan), auto-evaluation, operator
    /// answers, abort, and final sign-off. A plain newable class (not a singleton) so it's
    /// unit-testable in isolation - BsaPreflightService owns one instance per run attempt.
    ///
    /// State machine: InProgress(pageIndex) -&gt; AwaitingSignOff -&gt; Completed(result). Aborted is
    /// reachable from either of the first two states. Previous() can step back out of AwaitingSignOff
    /// into InProgress so the sign-off screen is reviewable, not a one-way door.
    ///
    /// Navigation is page-based, not check-based: PreflightPagePlan groups/paginates Run.Checks into
    /// PreflightPage instances (Auto checks hoisted to a leading page by default, remaining checks
    /// grouped and paginated per Metadata) without ever reordering Run.Checks itself - see
    /// PreflightRun.Checks's doc comment for why that distinction matters.
    /// </summary>
    public class PreflightRunEngine
    {
        readonly AutoCheckEvaluator _autoEvaluator;
        readonly RegisteredCheckRegistry _registry;
        readonly IReadOnlyList<PreflightPage> _pages;

        public PreflightRun Run { get; }

        /// <summary>Every page in display order - the jump rail (GoToGroup/GoToPage) renders from
        /// this, not just the current page.</summary>
        public IReadOnlyList<PreflightPage> Pages => _pages;

        public PreflightRunEngine(IList<PreflightCheckDefinition> checks, AutoCheckEvaluator autoEvaluator,
            RegisteredCheckRegistry registry, string operatorName, string missionBaselineHash = null,
            PreflightChecklistMetadata metadata = null)
        {
            if (checks == null || checks.Count == 0)
                throw new ArgumentException("A preflight run needs at least one check.", nameof(checks));

            _autoEvaluator = autoEvaluator ?? throw new ArgumentNullException(nameof(autoEvaluator));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            Run = new PreflightRun(checks.ToList())
            {
                OperatorName = operatorName,
                StartedUtc = DateTime.UtcNow,
                State = PreflightRunState.InProgress,
                MissionBaselineHash = missionBaselineHash
            };

            _pages = PreflightPagePlan.Build(Run.Checks, metadata);
            OnPageEntered(CurrentPage);
        }

        public PreflightPage CurrentPage =>
            Run.State == PreflightRunState.InProgress && Run.CurrentPageIndex < _pages.Count
                ? _pages[Run.CurrentPageIndex]
                : null;

        /// <summary>Convenience for the common single-check case - null whenever the current page
        /// holds zero or more than one check. Most pre-grouping call sites (and their tests) keep
        /// working unchanged through this; a multi-check page must address checks by id.</summary>
        public PreflightCheckDefinition CurrentCheck =>
            CurrentPage != null && CurrentPage.Checks.Count == 1 ? CurrentPage.Checks[0] : null;

        /// <summary>
        /// Evaluates one check (generic field-comparator, or a named registered check) without
        /// recording anything. Never throws - any failure to resolve source/field/condition/value (or
        /// a registered-check key with no matching registration) maps to Unknown with an explanatory
        /// detail. Works for any check in the run, not just CurrentCheck, since sign-off
        /// re-verification (TryCompleteRun) evaluates every Auto check regardless of which page it's on.
        /// </summary>
        public (CheckOutcome outcome, string detail) EvaluateCheck(PreflightCheckDefinition check)
        {
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

        /// <summary>Convenience over EvaluateCheck(CurrentCheck) for the single-check-page case.</summary>
        public (CheckOutcome outcome, string detail) EvaluateCurrentCheck() => EvaluateCheck(CurrentCheck);

        /// <summary>
        /// Records one check's answer, addressed explicitly by id - with several checks visible per
        /// page there is no safe "current check" to imply (an implicit target that works only
        /// sometimes is a footgun in safety code). Returns false, without recording, for exactly one
        /// expected/recoverable case: a RequiresNoteOnFail check answered FAIL with no note yet. The
        /// UI calls this reactively on every answer change, and that state is reached simply by
        /// clicking FAIL before typing a note - not a bug, so it must not throw out of an event
        /// handler. Every other rejection (unknown id, disallowed N/A, run not in progress) throws:
        /// those indicate a caller bug, not a normal interactive state.
        /// </summary>
        public bool RecordResult(string checkId, CheckOutcome outcome, string notes = null, string detail = null)
        {
            RequireInProgress();
            var check = FindCheck(checkId);

            if (outcome == CheckOutcome.NotApplicable && !check.AllowNotApplicable)
                throw new InvalidOperationException($"Check '{check.Id}' does not allow N/A.");

            if (outcome == CheckOutcome.Fail && check.RequiresNoteOnFail && string.IsNullOrWhiteSpace(notes))
                return false;

            RecordResultInternal(check, outcome, notes, detail, CheckResultSource.Operator);
            return true;
        }

        PreflightCheckDefinition FindCheck(string checkId)
        {
            var check = Run.Checks.FirstOrDefault(c => c.Id == checkId);
            if (check == null)
                throw new InvalidOperationException($"Check '{checkId}' is not part of this run.");
            return check;
        }

        void RecordResultInternal(PreflightCheckDefinition check, CheckOutcome outcome, string notes,
            string detail, CheckResultSource source)
        {
            Run.History.Add(new CheckResultRecord
            {
                CheckId = check.Id,
                CheckTitle = check.Title,
                Severity = check.Severity ?? CheckSeverity.Critical,
                Group = check.Group,
                Outcome = outcome,
                Notes = notes,
                Detail = detail,
                Source = source,
                TimestampUtc = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Evaluates and records one Auto check by the engine's own rules - never leaves a check
        /// unrecorded, since an unrecorded Auto check would block page advance with no operator action
        /// able to unstick it (unlike a Manual/Semi check, where the operator can always type a note):
        /// - DeferredToSignOff checks record Unknown with an explanation instead of their real
        ///   (premature/vacuous) value - but ONLY when source is AutoInitial. A re-verification
        ///   (AutoReverify) always uses the real evaluated value, which is the entire point of
        ///   DeferredToSignOff - see PreflightCheckDefinition.DeferredToSignOff.
        /// - A check that evaluates to FAIL and RequiresNoteOnFail (nobody is present to supply one
        ///   for an Auto check) records Unknown with an explanatory note instead.
        /// - Otherwise records exactly what EvaluateCheck returned.
        /// </summary>
        void RecordAutoCheck(PreflightCheckDefinition check, CheckResultSource source)
        {
            if (check.DeferredToSignOff && source == CheckResultSource.AutoInitial)
            {
                RecordResultInternal(check, CheckOutcome.Unknown, null, "Verified at sign-off.", source);
                return;
            }

            var (outcome, detail) = EvaluateCheck(check);

            if (outcome == CheckOutcome.Fail && check.RequiresNoteOnFail)
            {
                RecordResultInternal(check, CheckOutcome.Unknown,
                    "Auto check failed and required a note; none available without operator input.", detail, source);
                return;
            }

            RecordResultInternal(check, outcome, null, detail, source);
        }

        /// <summary>Called whenever navigation lands on a new current page - if it's the auto page,
        /// every check on it is (re-)evaluated and recorded as AutoInitial. Runs unconditionally on
        /// every visit (matching the pre-grouping wizard's existing per-step behaviour), which is safe
        /// now that HasChangedAnswer is scoped to Operator-sourced answers only.</summary>
        void OnPageEntered(PreflightPage page)
        {
            if (page == null || !page.IsAutoPage)
                return;

            foreach (var check in page.Checks)
                RecordAutoCheck(check, CheckResultSource.AutoInitial);
        }

        public bool CanGoNext => Run.State == PreflightRunState.InProgress;

        public bool CanGoPrevious =>
            Run.State == PreflightRunState.AwaitingSignOff ||
            (Run.State == PreflightRunState.InProgress && Run.CurrentPageIndex > 0);

        /// <summary>
        /// Advances past the current page if every check on it has a recorded answer; otherwise
        /// returns false with the ids still missing one, and does not move. Non-throwing so the UI can
        /// call this reactively (e.g. on a Next click with unanswered rows) without exception-driven
        /// control flow.
        /// </summary>
        public bool TryAdvance(out IReadOnlyList<string> unansweredCheckIds)
        {
            RequireInProgress();
            var page = CurrentPage;
            var unanswered = page == null
                ? new List<string>()
                : page.Checks.Where(c => !Run.History.Any(r => r.CheckId == c.Id)).Select(c => c.Id).ToList();

            if (unanswered.Count > 0)
            {
                unansweredCheckIds = unanswered;
                return false;
            }

            unansweredCheckIds = Array.Empty<string>();
            AdvancePastCurrentPage();
            return true;
        }

        /// <summary>Throwing convenience over TryAdvance for callers (tests, non-interactive
        /// drivers) that want a fail-fast contract instead of checking a bool.</summary>
        public void Next()
        {
            if (!TryAdvance(out var unansweredCheckIds))
                throw new InvalidOperationException(
                    $"{unansweredCheckIds.Count} check(s) on this page have not been answered yet: " +
                    string.Join(", ", unansweredCheckIds) + ".");
        }

        void AdvancePastCurrentPage()
        {
            if (Run.CurrentPageIndex >= _pages.Count - 1)
            {
                // §4: re-verify every Auto check the moment sign-off is reached, so a check like
                // mission-unchanged-during-preflight (whose baseline comparison is a tautology at
                // T+0) gets its first meaningful evaluation here rather than staying permanently Pass.
                foreach (var check in Run.Checks.Where(c => c.Type == CheckType.Auto))
                    RecordAutoCheck(check, CheckResultSource.AutoReverify);

                Run.State = PreflightRunState.AwaitingSignOff;
                return;
            }

            Run.CurrentPageIndex++;
            OnPageEntered(CurrentPage);
        }

        /// <summary>Steps back. From AwaitingSignOff this re-opens the last page for review/edit
        /// rather than moving an index - the sign-off screen is reviewable, not a one-way door.</summary>
        public void Previous()
        {
            if (Run.State != PreflightRunState.InProgress && Run.State != PreflightRunState.AwaitingSignOff)
                throw new InvalidOperationException($"Run is not active (state: {Run.State}).");

            if (Run.State == PreflightRunState.AwaitingSignOff)
            {
                Run.State = PreflightRunState.InProgress;
                OnPageEntered(CurrentPage);
                return;
            }

            if (Run.CurrentPageIndex > 0)
            {
                Run.CurrentPageIndex--;
                OnPageEntered(CurrentPage);
            }
        }

        /// <summary>Jumps to the first page of the named group (case-insensitive), for the wizard's
        /// group rail. No-op (returns false) if the run isn't in progress or no page has that group -
        /// a stale/mistyped rail entry must never throw mid-run. Jumping forward past unanswered pages
        /// is safe: nothing is skippable, only deferrable - TryCompleteRun re-checks every check in
        /// the whole run before it will ever complete, independent of which pages were visited via
        /// Next() vs a rail jump.</summary>
        public bool GoToGroup(string groupName)
        {
            if (Run.State != PreflightRunState.InProgress || string.IsNullOrWhiteSpace(groupName))
                return false;

            for (var i = 0; i < _pages.Count; i++)
            {
                if (!string.Equals(_pages[i].GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Run.CurrentPageIndex = i;
                OnPageEntered(CurrentPage);
                return true;
            }

            return false;
        }

        /// <summary>Jumps to a specific page index. Same safety notes as GoToGroup.</summary>
        public bool GoToPage(int pageIndex)
        {
            if (Run.State != PreflightRunState.InProgress || pageIndex < 0 || pageIndex >= _pages.Count)
                return false;

            Run.CurrentPageIndex = pageIndex;
            OnPageEntered(CurrentPage);
            return true;
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
        /// Only valid from AwaitingSignOff (reached by TryAdvance/Next() past the last page). Before
        /// finalizing, re-verifies every Auto check once more (§4a: "commit on the state you were
        /// shown") and refuses - without finalizing, non-throwing - if anything moved since it was
        /// last shown; the fresh values are still recorded either way, so a re-rendered sign-off panel
        /// reflects them. Only once nothing has moved does it fall through to a second, independent
        /// guard: every check in the entire run (not just pages actually visited - the jump rail can
        /// land on the last page without visiting every earlier one) must have a recorded answer.
        /// Idempotent: Completed/Aborted both return an already-done outcome without touching state.
        /// </summary>
        public SignOffOutcome TryCompleteRun()
        {
            if (Run.State == PreflightRunState.Completed || Run.State == PreflightRunState.Aborted)
                return SignOffOutcome.AlreadyCompleted;

            if (Run.State != PreflightRunState.AwaitingSignOff)
                throw new InvalidOperationException($"Cannot complete from state {Run.State}; step through every check first.");

            var changes = new List<AutoReverifyChange>();
            foreach (var check in Run.Checks.Where(c => c.Type == CheckType.Auto))
            {
                var before = Run.LatestPerCheck.FirstOrDefault(r => r.CheckId == check.Id);
                RecordAutoCheck(check, CheckResultSource.AutoReverify);
                var after = Run.LatestPerCheck.First(r => r.CheckId == check.Id);

                if (before != null && before.Outcome != after.Outcome)
                    changes.Add(new AutoReverifyChange(check.Id, before.Outcome, after.Outcome, after.Detail));
            }

            if (changes.Count > 0)
                return new SignOffOutcome(false, Array.Empty<string>(), changes);

            var unanswered = Run.Checks.Select(c => c.Id)
                .Where(id => !Run.History.Any(r => r.CheckId == id))
                .ToList();
            if (unanswered.Count > 0)
                return new SignOffOutcome(false, unanswered, Array.Empty<AutoReverifyChange>());

            Run.Result = PreflightAggregator.Aggregate(Run.LatestPerCheck, signOffCompleted: true);
            Run.State = PreflightRunState.Completed;
            Run.EndedUtc = DateTime.UtcNow;
            return SignOffOutcome.AlreadyCompleted;
        }

        /// <summary>Throwing convenience over TryCompleteRun for callers that want a fail-fast
        /// contract instead of inspecting the returned SignOffOutcome.</summary>
        public void CompleteRun()
        {
            var outcome = TryCompleteRun();
            if (outcome.Completed)
                return;

            if (outcome.ChangedAutoChecks.Count > 0)
                throw new InvalidOperationException(
                    $"{outcome.ChangedAutoChecks.Count} automatic check(s) changed since they were last shown - " +
                    "review before signing off again.");

            throw new InvalidOperationException(
                $"{outcome.UnansweredCheckIds.Count} check(s) have not been answered yet: " +
                string.Join(", ", outcome.UnansweredCheckIds) + ".");
        }

        void RequireInProgress()
        {
            if (Run.State != PreflightRunState.InProgress)
                throw new InvalidOperationException($"Run is not in progress (state: {Run.State}).");
        }
    }
}
