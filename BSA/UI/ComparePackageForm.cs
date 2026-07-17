using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Read-only compare report (WP2 Phase B, task B3) - a report view over the already-built
    /// ConfigCompareEngine/BsaConfigComposition.DiffImport, no apply path at all. Never mutates
    /// Settings.
    /// </summary>
    public class ComparePackageForm : Form
    {
        public ComparePackageForm(string packagePath)
        {
            Text = "Compare Current Config to Package";
            Width = 560;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 9)
            };
            Controls.Add(textBox);

            textBox.Text = BuildReport(packagePath);
        }

        static string BuildReport(string packagePath)
        {
            try
            {
                var package = BsaConfigPackage.Read(packagePath);
                var groups = BsaConfigComposition.DiffImport(package);

                var report = new StringBuilder();
                report.AppendLine("Package: " + Path.GetFileName(packagePath));
                report.AppendLine("Package version: " + package.Manifest.Version);
                report.AppendLine($"Created: {package.Manifest.CreatedAtUtc:u} by {package.Manifest.CreatedByOperator}");
                report.AppendLine();

                if (groups.Count == 0)
                {
                    report.AppendLine("MATCH - live config is identical to this package (portable settings).");
                }
                else
                {
                    foreach (var group in groups)
                    {
                        if (group.MismatchedKeys.Count > 0)
                            report.AppendLine("CHANGED: " + string.Join(", ", group.MismatchedKeys));
                        if (group.PackageOnlyKeys.Count > 0)
                            report.AppendLine("IN PACKAGE ONLY: " + string.Join(", ", group.PackageOnlyKeys));
                        if (group.LiveOnlyKeys.Count > 0)
                            report.AppendLine("LOCAL ONLY: " + string.Join(", ", group.LiveOnlyKeys));
                    }
                }

                return report.ToString();
            }
            catch (Exception ex)
            {
                return "Could not compare this package:\n" + ex.Message;
            }
        }
    }
}
