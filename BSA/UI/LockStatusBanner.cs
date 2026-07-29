using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Persistent Lock/Preflight/policy-version status - MainV2 has no StatusStrip to extend (verified:
    /// just a MenuStrip + one auto-hiding Controls.Status progress control), so this is a standalone
    /// UserControl (mirrors the existing Controls/Status.cs precedent, just persistent instead of
    /// auto-hiding) wired into MainV2's constructor with a couple of plain lines rather than hand-edited
    /// into MainV2.Designer.cs (a frequently upstream-touched, designer-regenerated file).
    ///
    /// Status only - it carries no actions. The Engineering-Mode-gated lock policy editor it used to
    /// host now lives with the rest of the BSA configuration actions on Config &gt; BullShark
    /// (BSA/UI/ConfigBullsharkPage.cs), so nothing clickable sits permanently under every screen.
    /// </summary>
    public class LockStatusBanner : Panel
    {
        readonly Label _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold),
            Tag = "custom"
        };

        /// <summary>Test visibility only - the real UI surface is the rendered control.</summary>
        public string DisplayText => _label.Text;

        public LockStatusBanner()
        {
            Height = 22;
            Dock = DockStyle.Bottom;
            Controls.Add(_label);
            Render(LockState.Off, null, PreflightResult.Unknown, null);
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
