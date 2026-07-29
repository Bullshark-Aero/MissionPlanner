using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.UI
{
    public class CheckAnswerChangedEventArgs : EventArgs
    {
        public string CheckId { get; }
        public CheckAnswerChangedEventArgs(string checkId) => CheckId = checkId;
    }

    /// <summary>
    /// Hosts one PreflightPage's worth of CheckRowPanel rows, stacked and scrollable. Answers are
    /// relayed up per-row (not batched) so PreflightWizardForm can record each one the moment it's
    /// given - see PreflightRunEngine.RecordResult's "answers are recorded when given, not on Next"
    /// design (WP1_wizard_grouping_pagination_plan.md §3).
    /// </summary>
    public class CheckGroupPanel : Panel
    {
        readonly Label _lblPageHeader = new Label
        {
            Dock = DockStyle.Top, Height = 26, Padding = new Padding(4, 4, 0, 0),
            Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold)
        };
        readonly FlowLayoutPanel _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false
        };
        readonly List<CheckRowPanel> _rowPanels = new List<CheckRowPanel>();

        public event EventHandler<CheckAnswerChangedEventArgs> AnswerChanged;

        public CheckGroupPanel()
        {
            Dock = DockStyle.Fill;

            // FlowLayoutPanel never stretches children to its own width, so CheckRowPanel's fixed
            // default (Width = 620) would otherwise exceed the available width - and force an
            // unwanted horizontal scrollbar (this panel should only ever need to scroll vertically) -
            // whenever the window is narrower than that. Keep rows sized to the panel's actual
            // current width instead. (Note: HorizontalScroll.Visible = false does NOT reliably
            // suppress this under AutoScroll=true - WinForms' own scroll recalculation fights it back
            // on the next layout pass. Removing the need to scroll horizontally at all is the only
            // fix that sticks.)
            _rows.Resize += (s, e) => ResizeRowsToFit();

            Controls.Add(_rows);
            Controls.Add(_lblPageHeader);
        }

        void ResizeRowsToFit()
        {
            var width = Math.Max(300, _rows.ClientSize.Width - 4);
            foreach (Control c in _rows.Controls)
                c.Width = width;
        }

        public void Populate(PreflightPage page, PreflightRunEngine engine)
        {
            _rows.Controls.Clear();
            _rowPanels.Clear();

            _lblPageHeader.Text = page.PagesInGroup > 1
                ? $"{page.GroupName} — page {page.PageInGroup} of {page.PagesInGroup}"
                : page.GroupName;

            foreach (var check in page.Checks)
            {
                var row = new CheckRowPanel();
                row.AnswerChanged += (s, e) => AnswerChanged?.Invoke(this, new CheckAnswerChangedEventArgs(check.Id));

                CheckOutcome? suggestion = null;
                string suggestionDetail = null;
                if (check.Type == CheckType.Semi)
                {
                    var evaluated = engine.EvaluateCheck(check);
                    suggestion = evaluated.outcome;
                    suggestionDetail = evaluated.detail;
                }

                var priorAnswer = engine.Run.LatestPerCheck.FirstOrDefault(r => r.CheckId == check.Id);
                row.Populate(check, suggestion, suggestionDetail, priorAnswer);

                _rows.Controls.Add(row);
                _rowPanels.Add(row);
            }

            ResizeRowsToFit();
        }

        /// <summary>Re-evaluates and re-displays every non-deferred Auto row currently on screen,
        /// without touching the engine's recorded history - see CheckRowPanel.RefreshLiveDisplay.
        /// Fixes the System checks page otherwise showing whatever was true the instant the wizard
        /// opened until the operator happens to navigate away and back. DeferredToSignOff checks are
        /// skipped: their display is deliberately frozen at "verified at sign-off" until the real
        /// re-verification happens (WP1_wizard_grouping_pagination_plan.md §4b) - refreshing them
        /// early here would defeat that.</summary>
        public void RefreshAutoRows(PreflightRunEngine engine)
        {
            foreach (var row in _rowPanels)
            {
                if (row.Check == null || row.Check.Type != CheckType.Auto || row.Check.DeferredToSignOff)
                    continue;

                var (outcome, detail) = engine.EvaluateCheck(row.Check);
                row.RefreshLiveDisplay(outcome, detail);
            }
        }

        public bool TryGetAnswer(string checkId, out CheckOutcome outcome, out string notes)
        {
            var row = _rowPanels.FirstOrDefault(r => r.Check?.Id == checkId);
            if (row == null)
            {
                outcome = CheckOutcome.Unknown;
                notes = null;
                return false;
            }
            return row.TryGetAnswer(out outcome, out notes);
        }

        /// <summary>Highlights the named rows (a Next/Sign Off click found them unanswered) and
        /// scrolls the first one into view - never changes any recorded answer.</summary>
        public void FlagUnanswered(IEnumerable<string> checkIds)
        {
            var idSet = new HashSet<string>(checkIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            CheckRowPanel first = null;
            foreach (var row in _rowPanels)
            {
                if (row.Check == null || !idSet.Contains(row.Check.Id))
                    continue;
                row.FlagUnanswered();
                first = first ?? row;
            }

            if (first != null)
                _rows.ScrollControlIntoView(first);
        }
    }
}
