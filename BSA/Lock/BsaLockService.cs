using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Off: no preflight GO yet, or the lock was never armed - every check is a fail-open no-op,
    /// zero behavior change from stock Mission Planner. On: armed after a GO preflight and a passing
    /// policy-integrity check - checks are evaluated for real. InvalidatedPending: a controlled action
    /// invalidated the preflight while On - behaves exactly like Off for gating purposes (fail-open)
    /// until a new GO re-arms it; kept as a distinct, visible state purely so the status banner can
    /// tell the operator *why* it's off, not just that it is.
    /// </summary>
    public enum LockState
    {
        Off,
        On,
        InvalidatedPending
    }

    /// <summary>
    /// Process-lifetime instance singleton (mirrors BsaPreflightService) that gates specific Mission
    /// Planner actions against a LockPolicyConfig once armed. The single invariant every check method
    /// exists to preserve: while State != On, every check is a fail-open no-op - most of the time
    /// (bench setup, no preflight run yet) this service must be completely invisible. Public
    /// constructor (not just Instance) so tests can exercise it without touching shared static state -
    /// same convention as BsaPreflightService's own tests.
    /// </summary>
    public class BsaLockService
    {
        static readonly Lazy<BsaLockService> _instance = new Lazy<BsaLockService>(() => new BsaLockService());
        public static BsaLockService Instance => _instance.Value;

        readonly string _auditDirectory;

        public LockState State { get; private set; } = LockState.Off;
        public string StatusReason { get; private set; }
        public LockPolicyConfig Policy { get; private set; }

        public event EventHandler<LockStatusChangedEventArgs> StatusChanged;

        public BsaLockService(string auditDirectory = null)
        {
            _auditDirectory = auditDirectory ?? BsaPaths.AuditDirectory;
        }

        /// <summary>Wires this service to react to a preflight result. Takes the preflight service and
        /// policy providers as parameters, not hardcoded singletons, so a fresh BsaLockService is fully
        /// testable against a fresh, non-singleton BsaPreflightService (same pattern
        /// PreflightRunEngineTests already uses for the WP3 handshake stub).</summary>
        public void AttachToPreflight(BsaPreflightService preflightService, Func<string> policyPathProvider,
            Func<LockPolicyConfig> loadPolicy)
        {
            preflightService.StatusChanged += (s, e) => HandlePreflightResult(e.Result, policyPathProvider, loadPolicy);
        }

        void HandlePreflightResult(PreflightResult result, Func<string> policyPathProvider, Func<LockPolicyConfig> loadPolicy)
        {
            // NoGo/Warning/Unknown never arms the lock. An already-InvalidatedPending lock is left
            // alone here too - only a GO re-arms; anything else just isn't a re-arm attempt.
            if (result != PreflightResult.Go)
                return;

            string policyPath;
            try
            {
                policyPath = policyPathProvider();
            }
            catch (Exception ex)
            {
                SetState(LockState.Off, $"Could not resolve lock policy path: {ex.Message}");
                return;
            }

            var integrityError = LockPolicyIntegrity.Verify(policyPath);
            if (integrityError != null)
            {
                SetState(LockState.Off, integrityError);
                return;
            }

            try
            {
                Policy = loadPolicy();
            }
            catch (Exception ex)
            {
                SetState(LockState.Off, $"Lock policy failed to load: {ex.Message}");
                return;
            }

            SetState(LockState.On, null);
        }

        /// <summary>Evaluates a non-param-write action. Fail-open (Allow, non-invalidating) whenever
        /// the lock isn't On. Audit-logs every evaluated check (not the fail-open no-ops).</summary>
        public LockDecision CheckAction(string actionId, string matchValue)
        {
            if (State != LockState.On)
                return new LockDecision(LockClass.Allow, false);

            var decision = ResolveAction(actionId, matchValue);
            Audit(actionId, matchValue, decision, null, decision.Class == LockClass.Block ? "Blocked" : "Evaluated");

            // Invalidation applies only to classes that proceed unconditionally at this point.
            // Authorise is deferred to RecordAuthoriseResolution - firing it here would invalidate a
            // preflight for an action that may yet be refused at the passphrase prompt.
            if (decision.InvalidatesPreflight && (decision.Class == LockClass.Allow || decision.Class == LockClass.Warn))
                Invalidate($"'{actionId}'" + (string.IsNullOrEmpty(matchValue) ? "" : $" ({matchValue})") + " changed while locked.");

            return decision;
        }

        /// <summary>The BsaLockGate delegate target - null return means allow. Never blocks the
        /// calling thread, never touches UI (see the WP3 plan's threading analysis: setParamAsync is
        /// called synchronously from UI threads via .AwaitSync(), which has no message pump). An
        /// Authorise-classed param cannot be resolved interactively HERE - instead, a gated UI
        /// surface may pre-authorise specific names via BeginParamWriteAuthorisation (passphrase
        /// prompt happens there, before the write reaches this hook). With an active grant the write
        /// proceeds and is audited "Authorised"; without one it is refused, never degraded to Allow -
        /// silently proceeding would weaken a rule whose whole point is requiring a credential.
        /// Block is refused unconditionally: a grant lifts Authorise only.</summary>
        public string CheckParamWrite(string paramName, double value)
        {
            if (State != LockState.On)
                return null;

            var decision = LockActionMatcher.MatchParamWrite(paramName, Policy);

            if (decision.Class == LockClass.Authorise && IsParamWriteAuthorised(paramName))
            {
                Audit("param_write", paramName, decision, null, "Authorised");

                if (decision.InvalidatesPreflight)
                    Invalidate($"Parameter '{paramName}' changed under Engineering authorisation while locked.");

                return null;
            }

            if (decision.Class == LockClass.Block || decision.Class == LockClass.Authorise)
            {
                Audit("param_write", paramName, decision, null, "Blocked");
                return decision.Class == LockClass.Authorise
                    ? $"Refused by BSA Operational Lock: '{paramName}' requires Engineering Mode authorisation. Use a gated write surface (Full Parameter List) to authorise it."
                    : $"Refused by BSA Operational Lock: '{paramName}' is blocked while locked.";
            }

            Audit("param_write", paramName, decision, null, "Evaluated");

            if (decision.InvalidatesPreflight)
                Invalidate($"Parameter '{paramName}' changed while locked.");

            return null;
        }

        // Param names currently pre-authorised for write by an Engineering-Mode gate (see
        // BeginParamWriteAuthorisation). Refcounted so overlapping scopes for the same name can't
        // cancel each other early; guarded by its own sync object because the wire hook may be
        // invoked from a different thread than the UI gate that opened the scope. The lock is only
        // ever held for a dictionary touch - never across UI or I/O - so it cannot violate
        // BsaLockGate's never-block contract.
        readonly Dictionary<string, int> _authorisedParamWrites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly object _authorisedParamWritesSync = new object();

        bool IsParamWriteAuthorised(string paramName)
        {
            lock (_authorisedParamWritesSync)
            {
                return paramName != null && _authorisedParamWrites.ContainsKey(paramName);
            }
        }

        /// <summary>
        /// Opens a scope during which Authorise-classed writes to the named params are permitted at
        /// the wire hook (audited "Authorised" per write; InvalidatesPreflight honoured per rule).
        /// Call ONLY from a gate that has just verified the Engineering passphrase
        /// (BSA.UI.LockGateUi.AuthoriseParamWrites) - this method deliberately performs no credential
        /// check itself so it stays unit-testable without UI. Dispose promptly after the writes; the
        /// grant lifts Authorise only, never Block.
        /// </summary>
        public IDisposable BeginParamWriteAuthorisation(IEnumerable<string> paramNames)
        {
            var names = (paramNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_authorisedParamWritesSync)
            {
                foreach (var name in names)
                {
                    _authorisedParamWrites.TryGetValue(name, out var count);
                    _authorisedParamWrites[name] = count + 1;
                }
            }

            return new ParamWriteAuthorisationScope(this, names);
        }

        void EndParamWriteAuthorisation(List<string> names)
        {
            lock (_authorisedParamWritesSync)
            {
                foreach (var name in names)
                {
                    if (!_authorisedParamWrites.TryGetValue(name, out var count))
                        continue;

                    if (count <= 1)
                        _authorisedParamWrites.Remove(name);
                    else
                        _authorisedParamWrites[name] = count - 1;
                }
            }
        }

        sealed class ParamWriteAuthorisationScope : IDisposable
        {
            BsaLockService _service;
            readonly List<string> _names;

            public ParamWriteAuthorisationScope(BsaLockService service, List<string> names)
            {
                _service = service;
                _names = names;
            }

            public void Dispose()
            {
                _service?.EndParamWriteAuthorisation(_names);
                _service = null; // idempotent - double-dispose must not decrement twice
            }
        }

        /// <summary>UI gates call this after resolving an Authorise-classed decision (see
        /// BSA.UI.LockGateUi) - CheckAction deliberately defers both the resolution outcome and any
        /// InvalidatesPreflight effect for Authorise, because authorisation hadn't been resolved yet
        /// when the check ran. Never reached on the fail-open path: fail-open decisions are always
        /// plain Allow, so no gate ever sees an Authorise class while the lock is off.</summary>
        public void RecordAuthoriseResolution(string actionId, string matchValue, LockDecision decision, bool authorised)
        {
            Audit(actionId, matchValue, decision, null, authorised ? "Authorised" : "AuthoriseRefused");

            if (authorised && decision.InvalidatesPreflight)
                Invalidate($"'{actionId}'" + (string.IsNullOrEmpty(matchValue) ? "" : $" ({matchValue})") +
                           " proceeded under Engineering authorisation while locked.");
        }

        /// <summary>UI gates call this when an operator supplies a typed reason for a Warn-classed
        /// action. The action itself already proceeded and was logged by CheckAction - this appends
        /// the human context alongside it (best-effort reason capture, per the WP3 plan's T4e scope).</summary>
        public void RecordOperatorReason(string actionId, string matchValue, LockDecision decision, string reason)
        {
            Audit(actionId, matchValue, decision, reason, "ReasonRecorded");
        }

        LockDecision ResolveAction(string actionId, string matchValue)
        {
            switch (actionId)
            {
                case "param_reset_defaults": return LockActionMatcher.ResolveSingle(Policy.Actions.ParamResetDefaults, Policy);
                case "firmware_upload": return LockActionMatcher.ResolveSingle(Policy.Actions.FirmwareUpload, Policy);
                case "mission_edit": return LockActionMatcher.ResolveSingle(Policy.Actions.MissionEdit, Policy);
                case "preflight_config_edit": return LockActionMatcher.ResolveSingle(Policy.Actions.PreflightConfigEdit, Policy);
                case "lock_policy_edit": return LockActionMatcher.ResolveSingle(Policy.Actions.LockPolicyEdit, Policy);
                case "mp_setting_change": return LockActionMatcher.MatchMpSettingChange(matchValue, Policy);
                default: return new LockDecision(Policy.Default ?? LockClass.Allow, false);
            }
        }

        /// <summary>Drops the lock to InvalidatedPending - a no-op unless currently On (idempotent:
        /// calling this twice, or calling it while already Off, doesn't clobber an existing reason or
        /// fire spurious events).</summary>
        public void Invalidate(string reason)
        {
            if (State != LockState.On)
                return;

            SetState(LockState.InvalidatedPending, reason);
        }

        void SetState(LockState newState, string reason)
        {
            State = newState;
            StatusReason = reason;
            StatusChanged?.Invoke(this, new LockStatusChangedEventArgs(newState, reason));
        }

        void Audit(string actionId, string matchValue, LockDecision decision, string userReason, string outcome)
        {
            try
            {
                BsaAuditLog.Append(_auditDirectory, new AuditEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    ActionId = actionId,
                    MatchValue = matchValue,
                    Class = decision.Class.ToString(),
                    Reason = userReason,
                    Outcome = outcome
                });
            }
            catch
            {
                // Audit logging must never block or fail the gated action itself - a failed audit
                // write is a degraded-observability problem, not a reason to let a BLOCK slip through
                // or crash an otherwise-legitimate ALLOW.
            }
        }
    }
}
