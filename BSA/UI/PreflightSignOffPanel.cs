using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Final step: a full-width result banner, a per-group summary, a live-preview verdict (via
    /// PreflightAggregator, as if signing off right now), the standard issues list, and a Sign Off
    /// button. Reachable via Back too - see PreflightRunEngine.Previous() re-opening the last page
    /// for review before a real sign-off.
    ///
    /// The banner's colour follows the exact convention LockStatusBanner already established
    /// elsewhere in BSA (Color.Red + white text for a critical/armed state, Color.Khaki + black text
    /// for "needs attention", Color.Gainsboro + black text for neutral/off) - Go gets the same
    /// full-saturation-background/white-text treatment as Red, for the same reason: the result here
    /// is exactly as consequential as the lock state that banner already color-codes.
    ///
    /// When PreflightRunEngine.TryCompleteRun refuses (an Auto check moved since it was last shown,
    /// or the jump rail let the operator reach here with something unanswered), the wizard re-calls
    /// Populate with that refusal - the callout at the top must say plainly what moved so a refused
    /// Sign Off click never reads as a broken button (WP1_wizard_grouping_pagination_plan.md §4a).
    /// </summary>
    public class PreflightSignOffPanel : Panel
    {
        readonly Label _lblSummary = new Label
        {
            Dock = DockStyle.Top, Height = 48, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0), Font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold)
        };
        readonly Label _lblRefusalCallout = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.LightCoral,
            Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), Visible = false
        };
        readonly Label _lblGroupsHeading = new Label { AutoSize = true, Text = "By group:", Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold) };
        readonly ListBox _lstGroupSummary = new ListBox { Width = 500, Height = 100 };
        readonly Label _lblIssuesHeading = new Label { AutoSize = true, Text = "Issues:", Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold) };
        readonly ListBox _lstIssues = new ListBox { Width = 500, Height = 160 };
        readonly Button _btnSignOff = new Button { Text = "Sign Off", AutoSize = true };
        readonly FlowLayoutPanel _flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16)
        };

        public event EventHandler SignOffClicked;

        public PreflightSignOffPanel()
        {
            Dock = DockStyle.Fill;

            _btnSignOff.Click += (s, e) => SignOffClicked?.Invoke(this, EventArgs.Empty);

            _flow.Controls.Add(_lblRefusalCallout);
            _flow.Controls.Add(Spacer());
            _flow.Controls.Add(_lblGroupsHeading);
            _flow.Controls.Add(_lstGroupSummary);
            _flow.Controls.Add(Spacer());
            _flow.Controls.Add(_lblIssuesHeading);
            _flow.Controls.Add(_lstIssues);
            _flow.Controls.Add(Spacer());
            _flow.Controls.Add(_btnSignOff);

            // FlowLayoutPanel never stretches children to its own width, so the fixed-width ListBoxes
            // above would otherwise exceed the available width - and force an unwanted horizontal
            // scrollbar (this panel should only ever need to scroll vertically) - whenever the window
            // is narrower than their fixed size. Keep them sized to the panel's actual current width
            // instead (also re-applied explicitly in Populate - see its call to ResizeToFit - since a
            // Resize event isn't guaranteed to fire again if the size doesn't change after that first
            // layout pass). Note: HorizontalScroll.Visible = false does NOT reliably suppress this
            // under AutoScroll=true - WinForms' own scroll recalculation fights it back on the next
            // layout pass. Removing the need to scroll horizontally at all is the only fix that sticks.
            _flow.Resize += (s, e) => ResizeToFit();

            // _lblSummary is Dock=Top on the outer panel (added after _flow so it claims the top edge
            // first - see PreflightWizardForm's own layout-order comment for this codebase's Dock
            // convention), not part of the flow, so its background reads as a full-width banner.
            Controls.Add(_flow);
            Controls.Add(_lblSummary);
        }

        void ResizeToFit()
        {
            var width = Math.Max(200, _flow.ClientSize.Width - _flow.Padding.Horizontal - 4);
            _lstGroupSummary.Width = width;
            _lstIssues.Width = width;
            _lblRefusalCallout.MaximumSize = new Size(width, 0);
        }

        static Control Spacer() => new Panel { Height = 12, Width = 1 };

        static Color BannerBackColor(PreflightResult result)
        {
            switch (result)
            {
                case PreflightResult.Go: return Color.Green;
                case PreflightResult.NoGo: return Color.Red;
                case PreflightResult.Warning: return Color.Khaki;
                default: return Color.Gainsboro;
            }
        }

        static Color BannerForeColor(PreflightResult result) =>
            result == PreflightResult.Go || result == PreflightResult.NoGo ? Color.White : Color.Black;

        public void Populate(PreflightRunEngine engine, IReadOnlyList<AutoReverifyChange> refusalChanges = null,
            IReadOnlyList<string> refusalUnansweredIds = null)
        {
            var run = engine.Run;
            ResizeToFit();

            if (refusalChanges != null && refusalChanges.Count > 0)
            {
                _lblRefusalCallout.Visible = true;
                _lblRefusalCallout.Text =
                    $"{refusalChanges.Count} automatic check(s) changed since this page was last shown - " +
                    "review below, then click Sign Off again:\r\n" +
                    string.Join("\r\n", refusalChanges.Select(c =>
                        $"  {TitleFor(run, c.CheckId)}: was {c.Before} -> now {c.After}" +
                        (string.IsNullOrEmpty(c.Detail) ? "" : $" ({c.Detail})")));
            }
            else if (refusalUnansweredIds != null && refusalUnansweredIds.Count > 0)
            {
                _lblRefusalCallout.Visible = true;
                _lblRefusalCallout.Text =
                    $"{refusalUnansweredIds.Count} check(s) still need an answer before signing off:\r\n" +
                    string.Join("\r\n", refusalUnansweredIds.Select(id => "  " + TitleFor(run, id)));
            }
            else
            {
                _lblRefusalCallout.Visible = false;
            }

            var preview = PreflightAggregator.Aggregate(run.LatestPerCheck, signOffCompleted: true);
            _lblSummary.Text = $"  Ready to sign off. If you sign off now, result: {preview}";
            _lblSummary.BackColor = BannerBackColor(preview);
            _lblSummary.ForeColor = BannerForeColor(preview);

            var latest = run.LatestPerCheck.ToDictionary(r => r.CheckId, r => r, StringComparer.OrdinalIgnoreCase);

            _lstGroupSummary.Items.Clear();
            foreach (var groupName in engine.Pages.Select(p => p.GroupName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var groupChecks = engine.Pages
                    .Where(p => string.Equals(p.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Checks)
                    .ToList();
                var fails = groupChecks.Count(c => latest.TryGetValue(c.Id, out var r) && r.Outcome == CheckOutcome.Fail);
                var unknowns = groupChecks.Count(c => !latest.TryGetValue(c.Id, out var r) || r.Outcome == CheckOutcome.Unknown);

                var line = $"{groupName}: {groupChecks.Count} check(s)";
                if (fails > 0) line += $", {fails} FAIL";
                if (unknowns > 0) line += $", {unknowns} UNKNOWN";
                _lstGroupSummary.Items.Add(line);
            }

            _lstIssues.Items.Clear();
            foreach (var r in run.LatestPerCheck.Where(r => r.Outcome == CheckOutcome.Fail || r.Outcome == CheckOutcome.Unknown))
                _lstIssues.Items.Add($"[{r.Severity}] {r.CheckTitle}: {r.Outcome}" +
                                      (string.IsNullOrEmpty(r.Notes) ? "" : $" - {r.Notes}"));

            foreach (var id in run.Checks.Select(c => c.Id).Where(run.HasChangedAnswer))
                _lstIssues.Items.Add($"(answer changed during this run: {TitleFor(run, id)})");

            foreach (var id in run.Checks.Select(c => c.Id).Where(run.HasAutoReverifyChange))
                _lstIssues.Items.Add($"(automatic check value changed since first shown: {TitleFor(run, id)})");

            if (_lstIssues.Items.Count == 0)
                _lstIssues.Items.Add("No issues recorded.");
        }

        static string TitleFor(PreflightRun run, string checkId) =>
            run.Checks.FirstOrDefault(c => c.Id == checkId)?.Title ?? checkId;
    }
}
