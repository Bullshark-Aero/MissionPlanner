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
    /// an arbitrary picked one - see ConfigActionsBar. Step navigation mirrors
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
        Step _step;

        /// <param name="validation">Pre-validated by the caller (ConfigActionsBar) BEFORE this form
        /// is constructed - validation failure must never reach this form at all. Closing a
        /// modally-shown form from inside its own Load event (the previous design) is a documented
        /// WinForms hazard, and validating first is better UX anyway: no window ever opens for a
        /// corrupt package.</param>
        public ImportWizardForm(string packagePath, ImportValidationResult validation)
        {
            _packagePath = packagePath ?? throw new ArgumentNullException(nameof(packagePath));
            _validation = validation ?? throw new ArgumentNullException(nameof(validation));

            Text = "Import MP Config";
            Width = 640;
            Height = 520;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 400);

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
                       $"Created: {m.CreatedAtUtc:u}\n" +
                       $"Created by: {m.CreatedByOperator}\n" +
                       $"Mission Planner version: {m.MissionPlannerVersion}\n\n" +
                       (string.IsNullOrWhiteSpace(m.ReleaseNotes) ? "" : m.ReleaseNotes + "\n\n");

            if (!_validation.VersionCompatible)
                text += "WARNING: " + _validation.VersionWarning;

            var info = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(16),
                Text = text
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

            if (!_diffPanel.HasAnyApplicableGroup)
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
            if (selected.Count == 0)
            {
                if (CustomMessageBox.Show(
                        "No settings are selected - nothing will be applied. Continue anyway?",
                        "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                    return;

                ShowLocalSetupStep();
                return;
            }

            if (CustomMessageBox.Show(
                    $"This will back up your current config, then apply {selected.Count} setting(s). Continue?",
                    "Import MP Config", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                return;

            // Backup must succeed before anything is applied - never skippable, per the source
            // document's "import creates a backup" requirement.
            try
            {
                _backupPath = BsaConfigComposition.BackupBeforeImport(Path.GetFileName(_packagePath));
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    "Could not create a backup - import aborted, nothing was changed:\n" + ex.Message,
                    "Import MP Config");
                return;
            }

            try
            {
                _appliedKeys = BsaConfigComposition.ApplyImport(_validation.Package, selected);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"Import failed while applying settings:\n{ex.Message}\n\nA backup of your PREVIOUS config was saved to:\n{_backupPath}",
                    "Import MP Config");
                Close();
                return;
            }

            CustomMessageBox.Show(
                $"{_appliedKeys.Count} setting(s) applied.\n\nA backup of your previous config was saved to:\n{_backupPath}",
                "Import MP Config");
            ShowLocalSetupStep();
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
            if (_appliedKeys != null && _appliedKeys.Count > 0)
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
