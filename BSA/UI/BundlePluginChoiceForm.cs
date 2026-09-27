using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    public class BundlePluginChoiceForm : Form
    {
        readonly Dictionary<string, CheckBox> _choices = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        readonly IReadOnlyList<BsaPluginExport> _plugins;

        public BundlePluginChoiceForm(IReadOnlyList<BsaPluginExport> plugins)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));

            Text = "Export MP Config";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(14),
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(new Label
            {
                Text = "Include plugins in this bundle?",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            });
            layout.Controls.Add(new Label
            {
                Text = "These plugins are installed and running on this ground station. Plugins are executable code: " +
                       "whoever imports the bundle is asked before any of them is installed.",
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Margin = new Padding(0, 0, 0, 10)
            });

            foreach (var plugin in plugins)
            {
                var box = new CheckBox
                {
                    Text = plugin.DisplayName + " " + plugin.Version + "  (" + plugin.PluginId + ".dll)",
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 2)
                };
                _choices[plugin.PluginId] = box;
                layout.Controls.Add(box);
            }

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0)
            };
            var btnContinue = new Button { Text = "Continue", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(btnContinue);
            buttons.Controls.Add(btnCancel);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = btnContinue;
            CancelButton = btnCancel;
            ActiveControl = btnCancel;
        }

        public List<BsaPluginExport> SelectedPlugins =>
            _plugins.Where(p => _choices.TryGetValue(p.PluginId, out var box) && box.Checked).ToList();

        public void Choose(string pluginId, bool include) => _choices[pluginId].Checked = include;
    }
}
