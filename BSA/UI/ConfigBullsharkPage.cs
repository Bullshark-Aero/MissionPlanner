using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;
using MissionPlanner.BSA.Reports;
using MissionPlanner.Controls;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// The single Config &gt; BullShark page: every BSA configuration action in one place. Replaces
    /// three scattered entry points - the app-wide bottom ConfigActionsBar (Import / Restore /
    /// Compare), FlightData's "Export MP Config" button, and the "Edit Policy..." button that used to
    /// ride on LockStatusBanner. None of those are flight-line actions, and the bottom bar in
    /// particular sat under every screen including Flight Data, offering a one-click path to
    /// overwrite the machine's settings mid-flight.
    ///
    /// Registered from GCSViews/SoftwareConfig.cs, which loads fine with no vehicle connected - so
    /// the original reason the import buttons lived at MainV2 level (a fresh laptop is configured
    /// before any vehicle is ever linked) still holds here.
    ///
    /// The constructor is deliberately inert - no MainV2.comPort / Settings access - so the page can
    /// be constructed headlessly in a test. All MP globals are reached only inside click handlers.
    /// </summary>
    public class ConfigBullsharkPage : MyUserControl
    {
        readonly Button _importButton = new Button { Text = "Import Config..." };
        readonly Button _restoreButton = new Button { Text = "Restore Previous..." };
        readonly Button _compareButton = new Button { Text = "Compare to Package..." };
        readonly Button _exportButton = new Button { Text = "Export MP Config" };
        readonly Button _changePassphraseButton = new Button { Text = "Change Passphrase..." };
        readonly Button _editPolicyButton = new Button { Text = "Edit Lock Policy..." };

        public ConfigBullsharkPage()
        {
            _importButton.Click += (s, e) => OnImportClicked();
            _restoreButton.Click += (s, e) => OnRestoreClicked();
            _compareButton.Click += (s, e) => OnCompareClicked();
            _exportButton.Click += (s, e) => OnExportClicked();
            _changePassphraseButton.Click += (s, e) => OnChangePassphraseClicked();
            _editPolicyButton.Click += (s, e) => OnEditPolicyClicked();

            var approved = BuildSection("Approved Configuration",
                Row(_importButton,
                    "Apply an approved .bsampconfig package to this machine. The current config is backed up first."),
                Row(_restoreButton,
                    "Roll back to one of the automatic backups taken before a previous import."),
                Row(_compareButton,
                    "Read-only diff of this machine's settings against a package. Changes nothing."));

            var authoring = BuildSection("Authoring & Engineering",
                Row(_exportButton,
                    "Publish this machine's settings as a .bsampconfig package, and optionally set it as this machine's approved reference config."),
                Row(_changePassphraseButton,
                    "Set or change the Engineering passphrase used to edit the lock policy and resolve authorise class prompts."),
                Row(_editPolicyButton,
                    "Engineering Mode only. Edit and re-approve the operational lock policy (lock_policy.json)."));

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16)
            };
            flow.Controls.Add(approved);
            flow.Controls.Add(authoring);

            Controls.Add(flow);
        }

        // ----- layout helpers -----

        /// <summary>One action row: a fixed-width button so every button in the page lines up, plus a
        /// wrapping one-line explanation of what clicking it actually does.</summary>
        static Control[] Row(Button button, string description)
        {
            button.Size = new Size(170, 26);
            button.Anchor = AnchorStyles.Left;
            button.Margin = new Padding(0, 3, 12, 3);
            button.UseVisualStyleBackColor = true;

            var label = new Label
            {
                Text = description,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 3)
            };

            return new Control[] { button, label };
        }

        /// <summary>Section "card": a bordered Panel with a bold Label as its title, NOT a GroupBox -
        /// confirmed live (screenshot against the running app) that GroupBox.Text renders invisibly
        /// here, because GroupBox draws its caption via the native Visual Styles renderer, which
        /// ignores WinForms ForeColor entirely (ThemeManager.cs sets it anyway, at line ~1136, but that
        /// assignment is a no-op for the same reason - this app has no other themed GroupBox to have
        /// caught it). Label.ForeColor has no such limitation - it's plain GDI+ text - which every
        /// description label on this page already proves renders correctly against the dark theme.</summary>
        static Panel BuildSection(string title, params Control[][] rows)
        {
            var titleLabel = new Label
            {
                Text = title,
                UseMnemonic = false, // "Authoring & Engineering" - Label treats & as an accelerator
                                     // prefix by default and silently drops it otherwise.
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };

            // Deliberately NOT docked. A Dock=Top child takes its width from the parent's client area,
            // while an AutoSize parent takes its width from the child - inside an AutoSize container
            // that circular dependency resolves to "just the padding", collapsing the section to ~20px
            // wide with no exception thrown. An undocked AutoSize table lets the panel measure it
            // properly. Covered by ConfigBullsharkPageTests.HostedLikeABackstagePage_*.
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = rows.Length
            };

            // Added by explicit cell coordinate rather than in sequence, so a row's button and its
            // description are always on the same line regardless of the table's fill order.
            for (var i = 0; i < rows.Length; i++)
            {
                table.Controls.Add(rows[i][0], 0, i);
                table.Controls.Add(rows[i][1], 1, i);
            }

            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false
            };
            body.Controls.Add(titleLabel);
            body.Controls.Add(table);

            var section = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10, 8, 10, 10),
                Margin = new Padding(0, 0, 0, 14)
            };
            section.Controls.Add(body);

            return section;
        }

        // ----- Import / Restore / Compare (moved from BSA/UI/ConfigActionsBar.cs) -----

        void OnImportClicked()
        {
            using (var ofd = new OpenFileDialog { Title = "Import MP Config", Filter = "BSA MP Config (*.bsampconfig)|*.bsampconfig" })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                RunImportWizard(ofd.FileName);
            }
        }

        void OnRestoreClicked()
        {
            var backupsDir = BsaPaths.BackupsDirectory;
            if (!Directory.Exists(backupsDir) || Directory.GetFiles(backupsDir, "*.bsampconfig").Length == 0)
            {
                CustomMessageBox.Show(
                    "No backups found yet. A backup is created automatically the first time you apply an import.",
                    "Restore Previous Config");
                return;
            }

            using (var ofd = new OpenFileDialog
            {
                Title = "Restore Previous Config",
                Filter = "BSA MP Config (*.bsampconfig)|*.bsampconfig",
                InitialDirectory = backupsDir
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                RunImportWizard(ofd.FileName);
            }
        }

        void OnCompareClicked()
        {
            using (var ofd = new OpenFileDialog { Title = "Compare Current Config to Package", Filter = "BSA MP Config (*.bsampconfig)|*.bsampconfig" })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                using (var form = new ComparePackageForm(ofd.FileName))
                    form.ShowDialog(FindForm());
            }
        }

        /// <summary>Validates BEFORE constructing the wizard - a corrupt/tampered package gets its
        /// error message here and no window ever opens (also avoids the WinForms close-inside-Load
        /// hazard the wizard's ctor doc comment describes).</summary>
        void RunImportWizard(string packagePath)
        {
            ImportValidationResult validation;
            try
            {
                validation = BsaConfigComposition.ValidateImport(packagePath);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("This package could not be validated:\n" + ex.Message, "Import MP Config");
                return;
            }

            using (var wizard = new ImportWizardForm(packagePath, validation))
                wizard.ShowDialog(FindForm());
        }

        // ----- Export (moved from GCSViews/FlightData.cs BUT_BsaExportConfig_Click) -----

        void OnExportClicked()
        {
            string operatorName = "";
            if (InputBox.Show("Export MP Config", "Operator name:", ref operatorName) != DialogResult.OK ||
                string.IsNullOrWhiteSpace(operatorName))
                return;

            string version = "1.0.0";
            if (InputBox.Show("Export MP Config", "Package version:", ref version) != DialogResult.OK ||
                string.IsNullOrWhiteSpace(version))
                return;

            string releaseNotes = "";
            InputBox.Show("Export MP Config", "Release notes (optional):", ref releaseNotes);

            using (var sfd = new SaveFileDialog
            {
                Title = "Export MP Config",
                Filter = "BSA MP Config (*.bsampconfig)|*.bsampconfig",
                // Sanitized for the filename only - the manifest keeps the operator's exact string.
                FileName = $"BSA_MP_Config_v{PreflightReportWriter.SanitizeForFilename(version)}.bsampconfig"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    BsaConfigComposition.ExportNow(sfd.FileName, operatorName, version, releaseNotes);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show("Could not export MP config: " + ex.Message, Strings.ERROR);
                    return;
                }

                if (CustomMessageBox.Show(
                        $"MP config exported to:\n{sfd.FileName}\n\nSet this as this machine's approved reference config?",
                        "Export MP Config", CustomMessageBox.MessageBoxButtons.YesNo) == CustomMessageBox.DialogResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(BsaPaths.RootDirectory);
                        // The operator may have saved the export directly into the approved slot -
                        // File.Copy onto itself throws even with overwrite:true.
                        if (!string.Equals(Path.GetFullPath(sfd.FileName),
                                Path.GetFullPath(BsaPaths.ApprovedConfigPackagePath),
                                StringComparison.OrdinalIgnoreCase))
                            File.Copy(sfd.FileName, BsaPaths.ApprovedConfigPackagePath, true);
                        // Changing what WP1.6's approved-package check compares against is an auditable
                        // action - CheckAction records it while the operational lock is armed (fail-open
                        // no-op otherwise).
                        BsaLockService.Instance.CheckAction("mp_setting_change", "set_approved_config");
                        CustomMessageBox.Show("Approved reference config updated.", "Export MP Config");
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show("Could not set as approved config: " + ex.Message, Strings.ERROR);
                    }
                }
            }
        }

        // ----- Engineering passphrase -----

        /// <summary>
        /// Set (first use) or change the Engineering Mode passphrase. Gated by the same
        /// lock_policy_edit rule as OnEditPolicyClicked below, not a separate action id: changing this
        /// passphrase also invalidates lock_policy.json's keyed approval stamp (LockPolicyIntegrity
        /// HMACs it with EngineeringMode.DerivedIntegrityKey), so it carries the same risk as editing
        /// the policy's content and should be refused under the same circumstances.
        /// </summary>
        void OnChangePassphraseClicked()
        {
            var lockDecision = BsaLockService.Instance.CheckAction("lock_policy_edit", "engineering_passphrase_change");
            if (lockDecision.Class == LockClass.Block)
            {
                CustomMessageBox.Show(
                    "The Engineering passphrase cannot be changed while the BSA Operational Lock is armed.",
                    "BSA Operational Lock");
                return;
            }

            var wasConfigured = EngineeringMode.IsConfigured;
            if (wasConfigured)
            {
                string currentPassphrase = "";
                if (InputBox.Show("Engineering Mode", "Enter the CURRENT Engineering passphrase:", ref currentPassphrase, true) != DialogResult.OK)
                    return;

                if (!EngineeringMode.Verify(currentPassphrase))
                {
                    CustomMessageBox.Show("Incorrect Engineering passphrase.", "Engineering Mode");
                    AuditPassphraseChange("Rejected");
                    return;
                }
            }

            string newPassphrase = "";
            if (InputBox.Show("Engineering Mode", "Enter the NEW Engineering passphrase:", ref newPassphrase, true) != DialogResult.OK)
                return;

            if (string.IsNullOrWhiteSpace(newPassphrase))
            {
                CustomMessageBox.Show("The Engineering passphrase cannot be blank.", "Engineering Mode");
                return;
            }

            string confirmPassphrase = "";
            if (InputBox.Show("Engineering Mode", "Confirm the NEW Engineering passphrase:", ref confirmPassphrase, true) != DialogResult.OK)
                return;

            if (confirmPassphrase != newPassphrase)
            {
                CustomMessageBox.Show("The passphrases you entered do not match. The Engineering passphrase was not changed.", "Engineering Mode");
                return;
            }

            EngineeringMode.SetPassphrase(newPassphrase);

            // The lock policy's approval stamp is keyed to the passphrase just replaced - re-stamp it
            // under the new one now, or the next arm attempt will see it as tampered and refuse to arm.
            LockPolicyIntegrity.Stamp(BsaLockComposition.ResolveLockPolicyPath());

            AuditPassphraseChange(wasConfigured ? "Changed" : "Configured");

            // Defensive, same as OnEditPolicyClicked below: the Block check above already prevents
            // this while armed, so this is currently unreachable rather than a live invalidation path.
            BsaLockService.Instance.Invalidate("Engineering passphrase changed and lock policy re-approved.");

            CustomMessageBox.Show("Engineering passphrase updated.", "Engineering Mode");
        }

        static void AuditPassphraseChange(string outcome)
        {
            try
            {
                BsaAuditLog.Append(BsaPaths.AuditDirectory, new AuditEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    ActionId = "engineering_passphrase_change",
                    MatchValue = null,
                    Class = "Engineering",
                    Outcome = outcome
                });
            }
            catch
            {
            }
        }

        // ----- Lock policy editing (moved from BSA/UI/LockStatusBanner.cs) -----

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
    }
}
