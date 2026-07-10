using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Renders one check step: title, severity, instruction/evidence text, and (for Manual/Semi)
    /// Pass/Fail/N-A buttons + a notes box. Auto checks render read-only - PreflightWizardForm records
    /// the auto-evaluated outcome directly, nothing for the operator to click. One adaptive panel instead
    /// of three near-identical ones for Manual/Auto/Semi, since the layout differs only in which controls
    /// are visible.
    /// </summary>
    public class CheckStepPanel : Panel
    {
        readonly Label _lblTitle = new Label();
        readonly Label _lblSeverity = new Label();
        readonly Label _lblInstruction = new Label();
        readonly Label _lblEvidence = new Label();
        readonly TextBox _txtNotes = new TextBox();
        readonly Label _lblNotes = new Label();
        readonly RadioButton _radPass = new RadioButton { Text = "PASS", AutoSize = true };
        readonly RadioButton _radFail = new RadioButton { Text = "FAIL", AutoSize = true };
        readonly RadioButton _radNa = new RadioButton { Text = "N/A", AutoSize = true };

        bool _suppressEvents;

        public event EventHandler AnswerChanged;

        public PreflightCheckDefinition Check { get; private set; }

        public CheckStepPanel()
        {
            Dock = DockStyle.Fill;

            _lblTitle.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
            _lblSeverity.Font = new Font(Font.FontFamily, 9, FontStyle.Italic);
            _lblInstruction.Font = new Font(Font.FontFamily, 11);
            _lblEvidence.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            _lblNotes.Text = "Notes:";

            foreach (var lbl in new[] { _lblTitle, _lblSeverity, _lblInstruction, _lblEvidence, _lblNotes })
            {
                lbl.AutoSize = true;
                lbl.MaximumSize = new Size(560, 0);
            }

            _txtNotes.Multiline = true;
            _txtNotes.Height = 60;
            _txtNotes.Width = 500;
            _txtNotes.ScrollBars = ScrollBars.Vertical;
            _txtNotes.TextChanged += (s, e) => RaiseAnswerChanged();

            foreach (var rad in new[] { _radPass, _radFail, _radNa })
                rad.CheckedChanged += (s, e) => RaiseAnswerChanged();

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16)
            };
            flow.Controls.Add(_lblTitle);
            flow.Controls.Add(_lblSeverity);
            flow.Controls.Add(Spacer());
            flow.Controls.Add(_lblInstruction);
            flow.Controls.Add(_lblEvidence);
            flow.Controls.Add(Spacer());

            var buttonRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            buttonRow.Controls.Add(_radPass);
            buttonRow.Controls.Add(_radFail);
            buttonRow.Controls.Add(_radNa);
            flow.Controls.Add(buttonRow);

            flow.Controls.Add(_lblNotes);
            flow.Controls.Add(_txtNotes);

            Controls.Add(flow);
        }

        static Control Spacer() => new Panel { Height = 12, Width = 1 };

        void RaiseAnswerChanged()
        {
            if (!_suppressEvents)
                AnswerChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// <paramref name="priorAnswer"/> is the check's own most-recent CheckResultRecord, if this step
        /// has already been answered once this run (e.g. the operator navigated Back to review it). When
        /// present it always wins over <paramref name="suggestedOutcome"/>/<paramref name="suggestedDetail"/>
        /// for seeding the radio buttons and notes box - a freshly recomputed Auto/Semi suggestion must
        /// never silently overwrite what the operator actually recorded (notes lost, or a deliberate
        /// override reverted) just because the step was revisited. Pass null on first visit.
        /// </summary>
        public void Populate(PreflightCheckDefinition check, CheckOutcome? suggestedOutcome, string suggestedDetail,
            CheckResultRecord priorAnswer)
        {
            _suppressEvents = true;
            try
            {
                Check = check;
                _lblTitle.Text = check.Title;
                _lblSeverity.Text = $"Severity: {check.Severity} | Type: {check.Type}";
                _lblInstruction.Text = check.Instruction ?? "";
                _lblEvidence.Text = suggestedDetail ?? "";
                _txtNotes.Text = "";
                _radPass.Checked = false;
                _radFail.Checked = false;
                _radNa.Checked = false;

                var interactive = check.Type == CheckType.Manual || check.Type == CheckType.Semi;
                _radPass.Visible = interactive;
                _radFail.Visible = interactive;
                _radNa.Visible = interactive && check.AllowNotApplicable;
                _lblNotes.Visible = interactive;
                _txtNotes.Visible = interactive;

                if (!interactive)
                    return;

                if (priorAnswer != null)
                {
                    _txtNotes.Text = priorAnswer.Notes ?? "";
                    if (priorAnswer.Outcome == CheckOutcome.Pass) _radPass.Checked = true;
                    else if (priorAnswer.Outcome == CheckOutcome.Fail) _radFail.Checked = true;
                    else if (priorAnswer.Outcome == CheckOutcome.NotApplicable) _radNa.Checked = true;
                }
                else if (suggestedOutcome == CheckOutcome.Pass)
                    _radPass.Checked = true;
                else if (suggestedOutcome == CheckOutcome.Fail)
                    _radFail.Checked = true;
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        public bool TryGetAnswer(out CheckOutcome outcome, out string notes)
        {
            notes = _txtNotes.Text;
            if (_radPass.Checked) { outcome = CheckOutcome.Pass; return true; }
            if (_radFail.Checked) { outcome = CheckOutcome.Fail; return true; }
            if (_radNa.Checked) { outcome = CheckOutcome.NotApplicable; return true; }
            outcome = CheckOutcome.Unknown;
            return false;
        }
    }
}
