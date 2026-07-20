using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Lock;
using MissionPlanner.Controls;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// UI-thread companion to BsaLockService.CheckAction for gates that can safely show dialogs -
    /// plain button/checkbox handlers, never the wire-level param hook (see BsaLockGate's threading
    /// contract). Resolves the two decision classes the service can't finish on its own: Authorise
    /// (inline Engineering passphrase prompt; refused or cancelled means the action must not proceed)
    /// and Warn (visible warning + optional typed reason into the audit log - WARN never vetoes the
    /// action, consistent with the wire-layer semantics in the WP3 plan). Allow proceeds silently,
    /// Block refuses with a message.
    /// </summary>
    public static class LockGateUi
    {
        /// <returns>true if the gated action may proceed.</returns>
        public static bool AllowedToProceed(string actionId, string matchValue, string actionDescription)
        {
            var service = BsaLockService.Instance;
            var decision = service.CheckAction(actionId, matchValue);

            switch (decision.Class)
            {
                case LockClass.Block:
                    CustomMessageBox.Show(
                        actionDescription + " is blocked while the BSA Operational Lock is armed. Re-run BSA Preflight after making any required changes.",
                        "BSA Operational Lock");
                    return false;

                case LockClass.Authorise:
                    return PromptForAuthorisation(service, actionId, matchValue, decision, actionDescription);

                case LockClass.Warn:
                    CaptureWarnReason(service, actionId, matchValue, decision, actionDescription);
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Gated confirmation surface for parameter writes: call from a UI write handler (e.g.
        /// ConfigRawParams' Write Params button) BEFORE the writes reach the wire, passing every name
        /// about to be written. Any of them classed Authorise by the armed policy triggers a single
        /// Engineering-passphrase prompt listing the affected params; on success the returned scope
        /// pre-authorises exactly those names at the wire hook (each write audited "Authorised",
        /// InvalidatesPreflight honoured per rule) - dispose it right after the writes. Returns null
        /// (harmless in a using) when nothing needs authorisation, the lock isn't armed, or the
        /// operator failed/cancelled the prompt - in the refusal case the writes still proceed and
        /// the wire refuses the Authorise-classed ones individually, exactly as an ungated surface
        /// would ("Set X Failed"), so a refused prompt can never make things MORE permissive.
        /// Block-classed names are never included: a grant lifts Authorise only.
        /// </summary>
        public static IDisposable AuthoriseParamWrites(IEnumerable<string> paramNames, string actionDescription)
        {
            var service = BsaLockService.Instance;
            if (service.State != LockState.On || service.Policy == null)
                return null;

            var needAuthorisation = (paramNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => LockActionMatcher.MatchParamWrite(n, service.Policy).Class == LockClass.Authorise)
                .ToList();

            if (needAuthorisation.Count == 0)
                return null;

            var paramList = string.Join(", ", needAuthorisation);

            if (!EngineeringMode.IsConfigured)
            {
                CustomMessageBox.Show(
                    $"{actionDescription} includes parameter(s) requiring Engineering Mode authorisation ({paramList}), but no Engineering passphrase is configured on this machine. Those writes will be refused.",
                    "BSA Operational Lock");
                RecordRefusals(service, needAuthorisation);
                return null;
            }

            string passphrase = "";
            if (InputBox.Show("BSA Operational Lock",
                    $"{actionDescription} includes parameter(s) requiring Engineering Mode authorisation:\n{paramList}\nEnter the Engineering passphrase (cancelling refuses those writes):",
                    ref passphrase, true) != DialogResult.OK)
            {
                RecordRefusals(service, needAuthorisation);
                return null;
            }

            if (!EngineeringMode.Verify(passphrase))
            {
                CustomMessageBox.Show("Incorrect Engineering passphrase. The authorisation-required parameter write(s) will be refused.", "BSA Operational Lock");
                RecordRefusals(service, needAuthorisation);
                return null;
            }

            // Successful writes are audited "Authorised" (and invalidate per rule) at the wire hook
            // itself - recording success here too would double-log every write in the batch.
            return service.BeginParamWriteAuthorisation(needAuthorisation);
        }

        static void RecordRefusals(BsaLockService service, List<string> paramNames)
        {
            foreach (var name in paramNames)
            {
                service.RecordAuthoriseResolution("param_write", name,
                    LockActionMatcher.MatchParamWrite(name, service.Policy), authorised: false);
            }
        }

        static bool PromptForAuthorisation(BsaLockService service, string actionId, string matchValue,
            LockDecision decision, string actionDescription)
        {
            if (!EngineeringMode.IsConfigured)
            {
                CustomMessageBox.Show(
                    actionDescription + " requires Engineering Mode authorisation, but no Engineering passphrase is configured on this machine.",
                    "BSA Operational Lock");
                service.RecordAuthoriseResolution(actionId, matchValue, decision, authorised: false);
                return false;
            }

            string passphrase = "";
            if (InputBox.Show("BSA Operational Lock",
                    actionDescription + " requires Engineering Mode authorisation.\nEnter the Engineering passphrase:",
                    ref passphrase, true) != DialogResult.OK)
            {
                service.RecordAuthoriseResolution(actionId, matchValue, decision, authorised: false);
                return false;
            }

            if (!EngineeringMode.Verify(passphrase))
            {
                CustomMessageBox.Show("Incorrect Engineering passphrase.", "BSA Operational Lock");
                service.RecordAuthoriseResolution(actionId, matchValue, decision, authorised: false);
                return false;
            }

            service.RecordAuthoriseResolution(actionId, matchValue, decision, authorised: true);
            return true;
        }

        static void CaptureWarnReason(BsaLockService service, string actionId, string matchValue,
            LockDecision decision, string actionDescription)
        {
            var reason = "";
            InputBox.Show("BSA Operational Lock",
                actionDescription + " while the lock is armed" +
                (decision.InvalidatesPreflight
                    ? " invalidates the preflight - re-run BSA Preflight before flight."
                    : ".") +
                "\nReason (recorded in the audit log):",
                ref reason);

            if (!string.IsNullOrWhiteSpace(reason))
                service.RecordOperatorReason(actionId, matchValue, decision, reason);
        }
    }
}
