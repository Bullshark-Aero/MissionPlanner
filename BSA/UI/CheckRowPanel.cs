using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Renders one check as a compact row, several of which are stacked by CheckGroupPanel on a
    /// single wizard page - the successor to the old one-check-per-form CheckStepPanel, same
    /// Populate(...)/TryGetAnswer(...) contract so the "prior answer wins over a fresh suggestion"
    /// semantics (see Populate's doc comment) survive unchanged. Auto checks render read-only
    /// (glyph + title + evaluator detail, no radios); Manual/Semi checks get inline PASS/FAIL/N-A with
    /// the notes box collapsed behind a "+ note" toggle, auto-expanding when FAIL is selected on a
    /// RequiresNoteOnFail check - the note box is the main vertical-space cost, so keeping it hidden
    /// until needed is what makes several rows fit on one page.
    /// </summary>
    public class CheckRowPanel : Panel
    {
        readonly Label _lblGlyph = new Label { AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold) };
        readonly Label _lblTitle = new Label { AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold) };
        readonly Label _lblSeverity = new Label { AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Italic) };
        readonly Label _lblInstruction = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        readonly Label _lblEvidence = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Font = new Font(FontFamily.GenericSansSerif, 8) };
        readonly RadioButton _radPass = new RadioButton { Text = "PASS", AutoSize = true };
        readonly RadioButton _radFail = new RadioButton { Text = "FAIL", AutoSize = true };
        readonly RadioButton _radNa = new RadioButton { Text = "N/A", AutoSize = true };
        readonly LinkLabel _lnkToggleNote = new LinkLabel { Text = "+ note", AutoSize = true };
        readonly Label _lblNoteRequired = new Label { Text = "Note required:", AutoSize = true, ForeColor = Color.LightCoral, Visible = false };
        readonly TextBox _txtNotes = new TextBox { Multiline = true, Height = 44, Width = 480, ScrollBars = ScrollBars.Vertical, Visible = false };

        readonly Panel _leftBar = new Panel { Width = 4, Dock = DockStyle.Left, BackColor = Color.Gainsboro };
        readonly FlowLayoutPanel _content = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        readonly FlowLayoutPanel _buttonRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

        bool _suppressEvents;

        public event EventHandler AnswerChanged;

        public PreflightCheckDefinition Check { get; private set; }

        public CheckRowPanel()
        {
            Width = 620;
            AutoSize = true;
            BorderStyle = BorderStyle.None;
            Padding = new Padding(8, 6, 8, 6);

            _txtNotes.TextChanged += (s, e) => RaiseAnswerChanged();
            foreach (var rad in new[] { _radPass, _radFail, _radNa })
                rad.CheckedChanged += (s, e) => OnRadioChanged();

            // Default LinkLabel blue is unreadable against the app's dark theme background -
            // match the same green Mission Planner already uses for its own buttons
            // (ThemeManager.ButBG) instead of an arbitrary color.
            _lnkToggleNote.LinkColor = ThemeManager.ButBG;
            _lnkToggleNote.ActiveLinkColor = ThemeManager.ButBG;
            _lnkToggleNote.VisitedLinkColor = ThemeManager.ButBG;
            _lnkToggleNote.LinkClicked += (s, e) => SetNoteVisible(!_txtNotes.Visible);

            _buttonRow.Controls.Add(_radPass);
            _buttonRow.Controls.Add(_radFail);
            _buttonRow.Controls.Add(_radNa);
            _buttonRow.Controls.Add(_lnkToggleNote);

            var titleRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            titleRow.Controls.Add(_lblGlyph);
            titleRow.Controls.Add(_lblTitle);
            titleRow.Controls.Add(_lblSeverity);

            _content.Controls.Add(titleRow);
            _content.Controls.Add(_lblInstruction);
            _content.Controls.Add(_lblEvidence);
            _content.Controls.Add(_buttonRow);
            _content.Controls.Add(_lblNoteRequired);
            _content.Controls.Add(_txtNotes);

            Controls.Add(_content);
            Controls.Add(_leftBar);
        }

        void OnRadioChanged()
        {
            if (_radFail.Checked && Check != null && Check.RequiresNoteOnFail)
                SetNoteVisible(true);
            RaiseAnswerChanged();
        }

        void SetNoteVisible(bool visible)
        {
            _txtNotes.Visible = visible;
            _lnkToggleNote.Visible = !visible;
            _lblNoteRequired.Visible = visible && Check != null && Check.RequiresNoteOnFail && _radFail.Checked;
        }

        static Color Gray => Color.Gainsboro;

        void RaiseAnswerChanged()
        {
            if (_suppressEvents)
                return;

            UpdateAnsweredBar();
            AnswerChanged?.Invoke(this, EventArgs.Empty);
        }

        void UpdateAnsweredBar()
        {
            _leftBar.BackColor = TryGetAnswer(out _, out _) ? Color.LightGreen : Color.OrangeRed;
        }

        /// <summary>
        /// <paramref name="priorAnswer"/> is the check's own most-recent CheckResultRecord, if this
        /// check has already been answered this run (operator navigated back to review it, or it's an
        /// Auto check already evaluated). When present it always wins over
        /// <paramref name="suggestedOutcome"/>/<paramref name="suggestedDetail"/> for seeding the
        /// radio buttons and notes box - a freshly recomputed Auto/Semi suggestion must never silently
        /// overwrite what was actually recorded (notes lost, or a deliberate override reverted) just
        /// because the row is being redrawn. Pass null on first visit.
        /// </summary>
        public void Populate(PreflightCheckDefinition check, CheckOutcome? suggestedOutcome, string suggestedDetail,
            CheckResultRecord priorAnswer)
        {
            _suppressEvents = true;
            try
            {
                Check = check;
                _lblTitle.Text = check.Title;
                _lblSeverity.Text = $"[{check.Severity}]";
                _lblInstruction.Text = check.Instruction ?? "";
                _lblInstruction.Visible = !string.IsNullOrEmpty(check.Instruction);
                _txtNotes.Text = "";
                _radPass.Checked = false;
                _radFail.Checked = false;
                _radNa.Checked = false;
                _lblNoteRequired.Visible = false;

                var interactive = check.Type == CheckType.Manual || check.Type == CheckType.Semi;
                _radPass.Visible = interactive;
                _radFail.Visible = interactive;
                _radNa.Visible = interactive && check.AllowNotApplicable;
                _buttonRow.Visible = interactive;
                _lnkToggleNote.Visible = interactive;
                _lblGlyph.Visible = !interactive;

                var effectiveOutcome = priorAnswer?.Outcome ?? suggestedOutcome;
                var effectiveDetail = priorAnswer?.Detail ?? suggestedDetail;
                _lblEvidence.Text = effectiveDetail ?? "";
                _lblEvidence.Visible = !string.IsNullOrEmpty(effectiveDetail);
                _lblGlyph.Text = GlyphFor(effectiveOutcome);
                _lblGlyph.ForeColor = ColorFor(effectiveOutcome);

                if (!interactive)
                {
                    SetNoteVisible(false);
                    // The bar means "needs your input" - meaningless for a read-only Auto row (there
                    // is nothing to input), and TryGetAnswer() can never be true here since no radio
                    // is ever checked, so UpdateAnsweredBar() would otherwise always paint it
                    // OrangeRed regardless of the actual Pass/Fail outcome. Hide it instead - the
                    // glyph colour already carries that information on the System checks page.
                    _leftBar.Visible = false;
                    return;
                }

                _leftBar.Visible = true;

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

                var hasNote = !string.IsNullOrEmpty(_txtNotes.Text);
                SetNoteVisible(hasNote || (_radFail.Checked && check.RequiresNoteOnFail));
                UpdateAnsweredBar();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        static string GlyphFor(CheckOutcome? outcome)
        {
            switch (outcome)
            {
                case CheckOutcome.Pass: return "✓"; // check mark
                case CheckOutcome.Fail: return "✗"; // cross mark
                case CheckOutcome.NotApplicable: return "—"; // em dash
                default: return "…"; // ellipsis - not yet evaluated/known
            }
        }

        // Only PASS/FAIL are colour-coded - bright enough to read at a glance against the app's dark
        // theme (DarkGreen/DarkRed were nearly invisible there). N/A and not-yet-known stay the
        // default text colour deliberately: they're neutral, not a result to react to.
        static Color ColorFor(CheckOutcome? outcome)
        {
            switch (outcome)
            {
                case CheckOutcome.Pass: return Color.LimeGreen;
                case CheckOutcome.Fail: return Color.Red;
                default: return Color.White;
            }
        }

        /// <summary>Updates only the read-only glyph/evidence display for an Auto row from a fresh
        /// evaluation - never touches Check or any recorded answer. Used to keep the System checks
        /// page visually live while the operator is looking at it (see PreflightWizardForm's
        /// watchdog-driven refresh) without appending to the engine's audit trail on every tick; that
        /// still only happens at the two real re-verification points (AwaitingSignOff entry, the Sign
        /// Off click). No-op for interactive (Manual/Semi) rows - there's nothing "live" to refresh
        /// there, the operator's own answer is authoritative.</summary>
        public void RefreshLiveDisplay(CheckOutcome outcome, string detail)
        {
            if (Check == null || Check.Type != CheckType.Auto)
                return;

            _lblEvidence.Text = detail ?? "";
            _lblEvidence.Visible = !string.IsNullOrEmpty(detail);
            _lblGlyph.Text = GlyphFor(outcome);
            _lblGlyph.ForeColor = ColorFor(outcome);
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

        /// <summary>Visually flags this row as needing attention (a Next click found it unanswered) -
        /// scrolls into view and highlights, without changing any answer state.</summary>
        public void FlagUnanswered()
        {
            _leftBar.BackColor = Color.OrangeRed;
            if (Check != null && (Check.Type == CheckType.Manual || Check.Type == CheckType.Semi))
                SetNoteVisible(_txtNotes.Visible);
            Focus();
        }
    }
}
