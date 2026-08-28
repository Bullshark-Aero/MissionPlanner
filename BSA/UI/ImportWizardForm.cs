using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;
using MissionPlanner.Controls;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Import flow per the source document: validate -&gt; show version/date/source -&gt; preview what
    /// will change -&gt; back up current config -&gt; apply selected -&gt; local-setup flags -&gt; restart
    /// prompt. "Restore Previous Config" is the exact same wizard pointed at a backup file instead of
    /// an arbitrary picked one - see ConfigBullsharkPage. Step navigation mirrors
    /// PreflightWizardForm's shape (WP1): one content panel swapped per step, Next button re-labeled
    /// per step.
    /// </summary>
    public class ImportWizardForm : Form
    {
        enum Step { Info, Diff, LocalSetup }

        readonly string _packagePath;
        readonly Label _lblHeader = new Label();
        readonly Panel _pnlContent = new Panel { Dock = DockStyle.Fill };
        readonly Button _btnNext = new Button { Text = "Next >", AutoSize = true };
        readonly Button _btnCancel = new Button { Text = "Cancel", AutoSize = true };
        readonly ImportDiffPanel _diffPanel = new ImportDiffPanel();

        readonly ImportValidationResult _validation;
        List<string> _appliedKeys;
        string _backupPath;
        bool _restartRequired;
        Step _step;

        /// <param name="validation">Pre-validated by the caller (ConfigBullsharkPage) BEFORE this form
        /// is constructed - validation failure must never reach this form at all. Closing a
        /// modally-shown form from inside its own Load event (the previous design) is a documented
        /// WinForms hazard, and validating first is better UX anyway: no window ever opens for a
        /// corrupt package.</param>
        public ImportWizardForm(string packagePath, ImportValidationResult validation)
        {
            _packagePath = packagePath ?? throw new ArgumentNullException(nameof(packagePath));
            _validation = validation ?? throw new ArgumentNullException(nameof(validation));

            Text = "Import MP Config";
            Width = 760;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 420);

            _lblHeader.Dock = DockStyle.Top;
            _lblHeader.Height = 28;
            _lblHeader.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            _lblHeader.Padding = new Padding(8, 6, 8, 0);

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            _btnNext.Click += (s, e) => OnNextClicked();
            _btnCancel.Click += (s, e) => Close();
            buttonRow.Controls.Add(_btnNext);
            buttonRow.Controls.Add(_btnCancel);

            Controls.Add(_pnlContent);
            Controls.Add(buttonRow);
            Controls.Add(_lblHeader);

            ShowInfoStep();
        }

        void ShowInfoStep()
        {
            _step = Step.Info;
            var m = _validation.Package.Manifest;

            var text = $"Package version: {m.Version}\n" +
                       $"Package ID: {m.PackageId}\n" +
                       $"Schema: {(m.SchemaVersion?.ToString() ?? "legacy")}\n" +
                       $"SHA-256: {_validation.Package.PackageSha256}\n" +
                       $"Created: {m.CreatedAtUtc:u}\n" +
                       $"Created by: {m.CreatedByOperator}\n" +
                       $"Mission Planner version: {m.MissionPlannerVersion}\n\n" +
                       (string.IsNullOrWhiteSpace(m.ReleaseNotes) ? "" : m.ReleaseNotes + "\n\n") +
                       BundleSummary(_validation.Package);

            if (!_validation.VersionCompatible)
                text += "WARNING: " + _validation.VersionWarning;

            var info = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Text = text.Replace("\n", Environment.NewLine)
            };

            _pnlContent.Controls.Clear();
            _pnlContent.Controls.Add(info);
            _lblHeader.Text = "Package info";
            _btnNext.Text = "Next >";
        }

        void OnNextClicked()
        {
            switch (_step)
            {
                case Step.Info:
                    ShowDiffStep();
                    break;
                case Step.Diff:
                    OnApplyClicked();
                    break;
                case Step.LocalSetup:
                    OfferRestartThenClose();
                    break;
            }
        }

        void ShowDiffStep()
        {
            _step = Step.Diff;

            List<ConfigDiffGroup> groups;
            try
            {
                groups = BsaConfigComposition.DiffImport(_validation.Package);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Could not compute the config diff:\n" + ex.Message, "Import MP Config");
                Close();
                return;
            }

            _diffPanel.Populate(groups, BsaConfigComposition.LiveConfigView(), _validation.Package.ConfigSubset);
            _pnlContent.Controls.Clear();
            _pnlContent.Controls.Add(_diffPanel);
            _lblHeader.Text = "Review changes - nothing is applied until you continue";
            _btnNext.Text = "Apply Selected >";

            if (!_diffPanel.HasAnyApplicableGroup && !_validation.Package.HasCompleteCoreProfile)
            {
                CustomMessageBox.Show(
                    "Nothing in this package differs from your live settings - there is nothing to import.",
                    "Import MP Config");
                Close();
            }
        }

        void OnApplyClicked()
        {
            var selected = _diffPanel.GetSelectedKeys();
            if (selected.Count == 0 && !_validation.Package.HasCompleteCoreProfile)
            {
                if (CustomMessageBox.Show(
                        "No settings are selected - nothing will be applied. Continue anyway?",
                        "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                    return;

                ShowLocalSetupStep();
                return;
            }

            var profileDescription = _validation.Package.HasCompleteCoreProfile
                ? " and the complete typed operational profile"
                : string.Empty;
            if (CustomMessageBox.Show(
                    $"This will back up every affected file, then apply {selected.Count} setting(s){profileDescription}. Continue?",
                    "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                return;

            try
            {
                var package = _validation.Package;
                var applied = BsaConfigComposition.ApplyBundleImport(_validation.Package, selected,
                    new BsaBundleApplyOptions
                    {
                        InstallChecklist = AskToInstallOptional(package.ChecklistJson, "preflight checklist"),
                        InstallKeyPolicy = AskToInstallOptional(package.KeyPolicyJson, "configuration key policy"),
                        InstallLockPolicy = AskToInstallOptional(package.LockPolicyJson, "operational lock policy; it must be re-approved in Engineering Mode")
                    });
                _appliedKeys = new List<string>(applied.ChangedSettings);
                _backupPath = applied.TransactionDirectory;
                _restartRequired = applied.RestartRequired;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"Import failed and was rolled back:\n{ex.Message}",
                    "Import MP Config");
                Close();
                return;
            }

            CustomMessageBox.Show(
                $"Bundle staged successfully. {_appliedKeys.Count} setting(s) changed.\n\nTransaction and rollback data:\n{_backupPath}\n\nRestart Mission Planner to verify and commit the installation.",
                "Import MP Config");
            ShowLocalSetupStep();
        }

        static string BundleSummary(ConfigPackageContents package)
        {
            if (package.IsLegacy) return "Legacy settings package; no typed operational profile.\n";
            var lines = new List<string>
            {
                "\nBundle components:",
                $"Quick panel: {package.QuickView?.Cells.Count ?? 0} cells",
                $"Stable telemetry bindings: {package.TelemetryBindings?.Bindings.Count ?? 0}",
                $"Warnings: {package.Warnings?.Rules.Count ?? 0}",
                $"Health rules: {package.HealthRules?.Rules.Count ?? 0}",
                $"Executable plugins: {package.Plugins.Count}"
            };
            foreach (var cell in package.QuickView?.Cells ?? new List<BsaQuickViewCell>())
                lines.Add($"  QuickView {cell.Position:D2}: {cell.SourceId} => {cell.Label}");
            foreach (var binding in package.TelemetryBindings?.Bindings ?? new List<BsaTelemetryBinding>())
                lines.Add($"  Binding: {binding.FieldId}; supported={binding.Supported}; freshness={binding.FreshnessSeconds}s");
            foreach (var warning in package.Warnings?.Rules ?? new List<BsaWarningRule>())
                lines.Add($"  Warning: {warning.Text}; {warning.Condition.FieldId} {warning.Condition.Operator} {warning.Condition.Value}; repeat={warning.RepeatSeconds}s; armed-only={warning.ArmedOnly}");
            foreach (var health in package.HealthRules?.Rules ?? new List<BsaHealthRule>())
                lines.Add($"  Health: {health.OutputFieldId} <= {health.Kind}; freshness={health.FreshnessSeconds}s; grace={health.ArmedGraceSeconds}s");
            if (package.Plugins.Count == 0) lines.Add("Trust status: data-only bundle; no executable code");
            return string.Join("\n", lines) + "\n";
        }

        static bool AskToInstallOptional(string content, string description)
        {
            return content != null && CustomMessageBox.Show(
                "This bundle includes an optional " + description + ". Install it? The current file is included in the transaction backup.",
                "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) == CustomMessageBox.DialogResult.Yes;
        }

        void ShowLocalSetupStep()
        {
            _step = Step.LocalSetup;
            // By this point either settings were applied or the operator explicitly chose to apply
            // nothing - either way "Cancel" would be a lie (there's nothing left to cancel).
            _btnCancel.Text = "Close";

            List<string> flags;
            try
            {
                flags = BsaConfigComposition.LocalSetupFlagsAfterImport();
            }
            catch
            {
                flags = new List<string>();
            }

            var text = flags.Count == 0
                ? "No machine-specific settings need review."
                : "These settings are machine-specific and were not touched by this import - " +
                  "review them before flight (packages never carry machine-specific values, so this " +
                  "reflects what's currently set on THIS machine, not what the source machine had):\n\n" +
                  string.Join("\n", flags);

            var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(16), Text = text };

            _pnlContent.Controls.Clear();
            _pnlContent.Controls.Add(label);
            _lblHeader.Text = "Local setup to review";
            _btnNext.Text = "Finish";
        }

        /// <summary>
        /// No Application.Restart() precedent exists anywhere in this codebase - this is genuinely new
        /// UX, not a reuse. It exists because many imported settings are only read into live
        /// in-memory/UI state at startup (or at the moment their own control changes them); writing the
        /// raw Settings.config key does not retroactively update already-initialized state, so a
        /// restart is the actual mechanism by which most imported settings take effect, not optional
        /// polish. Skipped entirely if nothing was applied.
        /// </summary>
        void OfferRestartThenClose()
        {
            if (_restartRequired)
            {
                var vehicleConnected = MainV2.comPort?.BaseStream?.IsOpen == true;
                var message = "Restart Mission Planner now so the imported settings take effect? " +
                               "Many settings are only applied at startup - without a restart, some changes may not appear until you restart manually." +
                               (vehicleConnected ? "\n\nA vehicle connection is currently open and will be disconnected." : "");

                if (CustomMessageBox.Show(message, "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) == CustomMessageBox.DialogResult.Yes)
                {
                    Close();
                    Application.Restart();
                    return;
                }
            }

            Close();
        }
    }
}
