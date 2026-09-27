using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    public class BundleProfileChoiceForm : Form
    {
        readonly Button _btnContinue = new Button { Text = "Continue", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        readonly Button _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        readonly Dictionary<string, RadioButton> _choices = new Dictionary<string, RadioButton>(StringComparer.Ordinal);

        public string SelectedOptionId { get; private set; }

        public bool CanContinue => _btnContinue.Enabled;

        public BundleProfileChoiceForm(IReadOnlyList<BsaBundleProfileOption> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

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
                Text = "Which aircraft is this configuration for?",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            });

            foreach (var option in options)
            {
                var radio = new RadioButton
                {
                    Text = option.DisplayName,
                    AutoSize = true,
                    Tag = option.Id,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = new Padding(0, 4, 0, 0)
                };
                radio.CheckedChanged += (s, e) =>
                {
                    if (!radio.Checked) return;
                    SelectedOptionId = (string)radio.Tag;
                    _btnContinue.Enabled = true;
                };
                _choices[option.Id] = radio;
                layout.Controls.Add(radio);
                layout.Controls.Add(new Label
                {
                    Text = option.Description,
                    AutoSize = true,
                    MaximumSize = new Size(460, 0),
                    Margin = new Padding(20, 0, 0, 8)
                });
            }

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttons.Controls.Add(_btnContinue);
            buttons.Controls.Add(_btnCancel);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = _btnContinue;
            CancelButton = _btnCancel;
            ActiveControl = _btnCancel;
        }

        public void Choose(string optionId) => _choices[optionId].Checked = true;
    }
}
