using System;
using System.IO;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Entry point for WP2 Phase B: Import Config / Restore Previous / Compare to Package. A new,
    /// separate self-contained UserControl rather than extending WP3's LockStatusBanner (keeps single
    /// responsibility clean), wired into MainV2's constructor the same way
    /// (MainV2.cs, alongside LockStatusBanner) - FlightData's tableLayoutPanel1 is a fully-occupied
    /// 5x5 grid (every cell already claimed by WP1/WP2A buttons and stock MP controls, verified), and
    /// there's no first-run/setup-wizard flow in this codebase to hook into instead. MainV2-level also
    /// fits the actual use case better than FlightData-level: importing config on a genuinely fresh
    /// laptop plausibly happens before any vehicle is ever connected.
    /// </summary>
    public class ConfigActionsBar : Panel
    {
        readonly Button _importButton = new Button { Text = "Import Config...", AutoSize = true };
        readonly Button _restoreButton = new Button { Text = "Restore Previous...", AutoSize = true };
        readonly Button _compareButton = new Button { Text = "Compare to Package...", AutoSize = true };

        public ConfigActionsBar()
        {
            Height = 26;
            Dock = DockStyle.Bottom;

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _importButton.Click += (s, e) => OnImportClicked();
            _restoreButton.Click += (s, e) => OnRestoreClicked();
            _compareButton.Click += (s, e) => OnCompareClicked();
            flow.Controls.Add(_importButton);
            flow.Controls.Add(_restoreButton);
            flow.Controls.Add(_compareButton);

            Controls.Add(flow);
        }

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
    }
}
