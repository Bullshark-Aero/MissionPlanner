using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;
using MissionPlanner.Controls;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Persistent Lock/Preflight/policy-version status - MainV2 has no StatusStrip to extend (verified:
    /// just a MenuStrip + one auto-hiding Controls.Status progress control), so this is a standalone
    /// UserControl (mirrors the existing Controls/Status.cs precedent, just persistent instead of
    /// auto-hiding) wired into MainV2's constructor with a couple of plain lines rather than hand-edited
    /// into MainV2.Designer.cs (a frequently upstream-touched, designer-regenerated file).
    ///
    /// Also hosts the one live entry point into Engineering-Mode-gated lock policy editing (WP3 T7) -
    /// kept here rather than as a new FlightData button because FlightData's tableLayoutPanel1 row 4
    /// is already full (WP1's BSA Preflight + WP2's Export MP Config), and this is a rare,
    /// engineering-only action that belongs next to the status it affects, not in the everyday
    /// operator button row.
    /// </summary>
    public class LockStatusBanner : Panel
    {
        readonly Label _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)
        };

        readonly Button _editPolicyButton = new Button
        {
            Text = "Edit Policy...",
            AutoSize = true,
            Dock = DockStyle.Right
        };

        /// <summary>Test visibility only - the real UI surface is the rendered control.</summary>
        public string DisplayText => _label.Text;

        public LockStatusBanner()
        {
            Height = 22;
            Dock = DockStyle.Bottom;
            _editPolicyButton.Click += (s, e) => OnEditPolicyClicked();
            Controls.Add(_label);
            Controls.Add(_editPolicyButton);
            Render(LockState.Off, null, PreflightResult.Unknown, null);
        }

        /// <summary>
        /// Engineering-Mode-gated lock-policy edit flow: refuse if the operational lock itself is
        /// currently Block-classed for lock_policy_edit (two independent gates - see the WP3 plan's
        /// AUTHORISE section), verify the Engineering passphrase (offering first-time setup if none is
        /// configured yet), open the policy file in the operator's default editor, then on
        /// confirmation validate and only approve (stamp) it if it's still a well-formed policy -
        /// an edit that breaks the JSON is reported and left unapproved, never silently accepted.
        /// </summary>
        void OnEditPolicyClicked()
        {
            // Two independent gates (see the WP3 plan's AUTHORISE section): the operational lock's
            // own action gate here, then the Engineering passphrase below. An Authorise-classed
            // LockPolicyEdit rule deliberately falls through this Block check - the mandatory
            // passphrase prompt below IS the authorisation for this particular flow.
            var lockDecision = BsaLockService.Instance.CheckAction("lock_policy_edit", null);
            if (lockDecision.Class == LockClass.Block)
            {
                CustomMessageBox.Show(
                    "Lock policy cannot be edited while the BSA Operational Lock is armed.",
                    "BSA Operational Lock");
                return;
            }

            string passphrase = "";
            if (InputBox.Show("Engineering Mode", "Enter the Engineering passphrase:", ref passphrase, true) != DialogResult.OK)
                return;

            if (!EngineeringMode.IsConfigured)
            {
                if (CustomMessageBox.Show(
                        "No Engineering passphrase is set yet on this machine. Set the passphrase you just entered as the Engineering Mode passphrase?",
                        "Engineering Mode", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                    return;

                EngineeringMode.SetPassphrase(passphrase);
                AuditPolicyEdit("PassphraseConfigured", null);
            }
            else if (!EngineeringMode.Verify(passphrase))
            {
                CustomMessageBox.Show("Incorrect Engineering passphrase.", "Engineering Mode");
                AuditPolicyEdit("PassphraseRejected", null);
                return;
            }

            var path = BsaLockComposition.ResolveLockPolicyPath();
            try
            {
                System.Diagnostics.Process.Start(path);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Could not open the lock policy for editing: " + ex.Message, "Engineering Mode");
                return;
            }

            CustomMessageBox.Show(
                "Edit and save the lock policy file in the editor that just opened, then click OK here to validate and approve your changes.",
                "Engineering Mode");

            try
            {
                var approved = LockPolicyLoader.Load(path);
                LockPolicyIntegrity.Stamp(path);
                AuditPolicyEdit("Approved", approved.PolicyVersion);

                // Source-document requirement: a policy change itself always invalidates the
                // preflight. No-op unless the lock is currently On (editing while On is Block-refused
                // above, so this is defensive rather than a live path today).
                BsaLockService.Instance.Invalidate("Lock policy edited and re-approved via Engineering Mode.");

                CustomMessageBox.Show("Lock policy validated and approved.", "Engineering Mode");
            }
            catch (Exception ex)
            {
                AuditPolicyEdit("RejectedInvalid", ex.Message);
                CustomMessageBox.Show(
                    "The edited lock policy is invalid and was NOT approved:\n" + ex.Message,
                    "Engineering Mode");
            }
        }

        /// <summary>
        /// Explicit audit trail for the policy-edit flow itself (acceptance criterion: "policy is
        /// controlled ... and logged"). Written directly rather than via CheckAction, which only logs
        /// evaluated checks while the lock is On - the interesting edit events all happen while it's
        /// off. Must never block or fail the edit flow.
        /// </summary>
        static void AuditPolicyEdit(string outcome, string detail)
        {
            try
            {
                BsaAuditLog.Append(BsaPaths.AuditDirectory, new AuditEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    ActionId = "lock_policy_edit",
                    MatchValue = detail,
                    Class = "Engineering",
                    Outcome = outcome
                });
            }
            catch
            {
            }
        }

        /// <summary>Wires this banner to the real singletons - takes them as parameters rather than
        /// reaching for .Instance internally, so a fresh banner can be attached to fresh, non-singleton
        /// services in a test.</summary>
        public void AttachToServices(BsaPreflightService preflightService, BsaLockService lockService)
        {
            preflightService.StatusChanged += (s, e) =>
                SetStatus(lockService.State, lockService.StatusReason, e.Result, lockService.Policy?.PolicyVersion);
            lockService.StatusChanged += (s, e) =>
                SetStatus(e.State, e.Reason, preflightService.Current, lockService.Policy?.PolicyVersion);
        }

        /// <summary>Thread-safe - BsaLockService.CheckParamWrite (the WARN path) can invalidate from a
        /// background thread, so this marshals via BeginInvoke (fire-and-forget, non-blocking) rather
        /// than a blocking Invoke, which would risk the same deadlock class documented on
        /// BsaLockGate/setParamAsync.</summary>
        public void SetStatus(LockState lockState, string lockReason, PreflightResult preflightResult, string policyVersion)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)(() => Render(lockState, lockReason, preflightResult, policyVersion)));
                return;
            }

            Render(lockState, lockReason, preflightResult, policyVersion);
        }

        void Render(LockState lockState, string lockReason, PreflightResult preflightResult, string policyVersion)
        {
            var lockText = lockState == LockState.On ? "ON"
                : lockState == LockState.InvalidatedPending ? "INVALIDATED"
                : "OFF";

            _label.Text = $"  BSA Lock: {lockText}   |   Preflight: {preflightResult}" +
                          (string.IsNullOrEmpty(policyVersion) ? "" : $"   |   Policy v{policyVersion}") +
                          (string.IsNullOrEmpty(lockReason) ? "" : $"   ({lockReason})");

            // On uses the same red as the HUD's DISARMED text (System.Drawing.Color.Red) for visual
            // consistency with the rest of the app's alert styling - white text for contrast against
            // that fully-saturated background, unlike the pastel Khaki/Gainsboro states below.
            BackColor = lockState == LockState.On ? Color.Red
                : lockState == LockState.InvalidatedPending ? Color.Khaki
                : Color.Gainsboro;
            _label.ForeColor = lockState == LockState.On ? Color.White : Color.Black;
        }
    }
}
