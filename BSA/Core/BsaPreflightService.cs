using System;
using System.Collections.Generic;
using MissionPlanner.BSA.Checks;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Process-lifetime instance singleton (Settings.Instance-style, not a static class like
    /// WarningEngine) so the event is genuinely mockable in tests - construct a fresh
    /// BsaPreflightService directly rather than touching Instance, subscribe, assert, discard. Holds
    /// Current (last published result) and fires StatusChanged; a future WP3 subscribes once at startup
    /// and gets updates across every run/reconnect/re-run.
    ///
    /// PublishResult is deliberately a separate, explicit step from CompleteRun()/Abort() on the engine -
    /// the caller (the wizard, see BSA/UI) must write the run's report first and only publish once that
    /// succeeded. A report-write failure must not let a run be observed as GO by any future WP3 listener
    /// (see the plan's "fail closed" report policy) - this service has no opinion on report writing, it
    /// just refuses to guess when it should be told.
    /// </summary>
    public class BsaPreflightService
    {
        static readonly Lazy<BsaPreflightService> _instance = new Lazy<BsaPreflightService>(() => new BsaPreflightService());
        public static BsaPreflightService Instance => _instance.Value;

        public event EventHandler<PreflightStatusChangedEventArgs> StatusChanged;

        public PreflightRunEngine CurrentRun { get; private set; }
        public PreflightResult Current { get; private set; } = PreflightResult.Unknown;

        /// <summary>
        /// Starts a new run, unless one is already InProgress/AwaitingSignOff - in that case returns the
        /// existing engine instead of starting a second concurrent run (nothing else guards against the
        /// wizard being opened twice).
        /// </summary>
        public PreflightRunEngine StartRun(IList<PreflightCheckDefinition> checks, AutoCheckEvaluator autoEvaluator,
            RegisteredCheckRegistry registry, string operatorName, string missionBaselineHash = null,
            PreflightChecklistMetadata metadata = null)
        {
            if (CurrentRun != null &&
                (CurrentRun.Run.State == PreflightRunState.InProgress || CurrentRun.Run.State == PreflightRunState.AwaitingSignOff))
            {
                return CurrentRun;
            }

            CurrentRun = new PreflightRunEngine(checks, autoEvaluator, registry, operatorName, missionBaselineHash, metadata);
            return CurrentRun;
        }

        /// <summary>Publishes a run's terminal result and fires StatusChanged. Call only after the
        /// run's report has been durably written (or, for an aborted run, after the abort report has
        /// been written) - see the type doc comment.</summary>
        public void PublishResult(PreflightRunEngine run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            Current = run.Run.Result;
            StatusChanged?.Invoke(this, new PreflightStatusChangedEventArgs(Current, run.Run));
        }
    }
}
