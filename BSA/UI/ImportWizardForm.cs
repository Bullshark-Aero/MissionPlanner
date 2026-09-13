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
                // An empty key/value diff does NOT mean an empty package: the checklist, policies and
                // warnings are whole files carried alongside the subset, and two machines configured
                // the same way differ in exactly those. Closing here would silently withhold them.
                if (!BsaConfigImporter.HasInstallableFiles(_validation.Package))
                {
                    CustomMessageBox.Show(
                        "Nothing in this package differs from your live settings - there is nothing to import.",
                        "Import MP Config");
                    Close();
                    return;
                }

                CustomMessageBox.Show(
                    "None of this package's settings differ from your live settings, so there is nothing to apply.\n\n" +
                    "The package does carry BSA configuration files, which are offered next.",
                    "Import MP Config");

                OfferBsaFileInstall();
                ShowLocalSetupStep();
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

                // Same reasoning as the empty-diff branch in ShowDiffStep: applying no settings is not
                // a reason to withhold the package's BSA files.
                OfferBsaFileInstall();
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

            OfferBsaFileInstall();
            ShowLocalSetupStep();
        }

        /// <summary>
        /// The package can also carry the organization's BSA config files (WP1 checklist, WP2 key
        /// policy, WP3 lock policy) - installing them is the other half of the fresh-laptop workflow
        /// (applying the mpconfig subset alone leaves BSA on the shipped defaults). Explicit
        /// opt-in per the "never blindly overwrite" requirement. The lock policy installs unstamped and
        /// must be re-approved in Engineering Mode before the lock will arm again (see
        /// BsaConfigInstaller).
        ///
        /// Reachable on three paths - after settings were applied, after the operator chose to apply
        /// none, and when nothing differed at all - so it cannot assume the pre-apply backup already
        /// ran. EnsureBackup below makes the backup unconditional before anything is overwritten,
        /// which is what lets the prompt promise one.
        /// </summary>
        void OfferBsaFileInstall()
        {
            var package = _validation.Package;
            var available = new List<string>();
            if (package.ChecklistJson != null) available.Add("preflight checklist");
            if (package.KeyPolicyJson != null) available.Add("config key policy");
            if (package.LockPolicyJson != null) available.Add("operational lock policy");
            if (package.WarningsXml != null) available.Add("warning definitions");

            if (available.Count == 0)
                return;

            var message = "This package also contains BSA configuration: " + string.Join(", ", available) + ".\n\n" +
                          "Install these onto this machine, replacing your current BSA config? " +
                          "(Your current BSA config is captured in an automatic backup first.)";
            if (package.WarningsXml != null)
                message += "\n\nThe package's warning definitions REPLACE this machine's existing warnings rather than adding to them; the backup holds your previous set.";
            if (package.LockPolicyJson != null)
                message += "\n\nThe imported lock policy must be re-approved in Engineering Mode (via the lock status bar's Edit Policy button) before the operational lock will arm.";

            if (CustomMessageBox.Show(message, "Import MP Config",
                    CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                return;

            if (!EnsureBackup())
                return;

            try
            {
                var result = BsaConfigComposition.InstallBsaFilesFromPackage(package,
                    installChecklist: package.ChecklistJson != null,
                    installKeyPolicy: package.KeyPolicyJson != null,
                    installLockPolicy: package.LockPolicyJson != null,
                    installWarnings: package.WarningsXml != null);

                var installedMessage = "Installed: " + string.Join(", ", result.InstalledFiles) +
                                       ".\nRestart Mission Planner for the new BSA configuration to take effect.";
                // Warnings are the exception - the engine was reloaded in place, so they are live now.
                if (result.InstalledFiles.Contains(BsaConfigInstaller.WarningsFileName))
                    installedMessage += result.WarningsReloadError == null
                        ? "\n\nThe imported warnings are already active - open the Warnings Manager to review them."
                        : "\n\nThe warnings were written but could not be loaded into the running session (" +
                          result.WarningsReloadError + "); they will take effect after the restart.";

                // On the apply path the operator has already been shown where the backup went; on the
                // two no-settings-applied paths this is their only sight of it.
                if (_appliedKeys == null)
                    installedMessage += "\n\nA backup of your previous config was saved to:\n" + _backupPath;

                CustomMessageBox.Show(installedMessage, "Import MP Config");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Could not install the BSA configuration files:\n" + ex.Message, "Import MP Config");
            }
        }

        /// <summary>
        /// Takes the pre-import backup if one has not been taken already, and reports whether it is now
        /// safe to overwrite. Idempotent: on the apply path OnApplyClicked has already run it, and this
        /// must not produce a second backup file for one import.
        ///
        /// Fail-closed, matching OnApplyClicked: if the backup cannot be written, nothing is installed.
        /// Installing the BSA files is exactly as destructive as applying settings - the lock policy and
        /// the machine's warning set are overwritten wholesale - so it gets the same guarantee.
        /// </summary>
        bool EnsureBackup()
        {
            if (_backupPath != null)
                return true;

            try
            {
                _backupPath = BsaConfigComposition.BackupBeforeImport(Path.GetFileName(_packagePath));
                return true;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    "Could not create a backup - nothing was installed and nothing was changed:\n" + ex.Message,
                    "Import MP Config");
                return false;
            }
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
