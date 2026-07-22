using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Left-hand navigation rail: every group with an answered/total count and a fail marker, the
    /// current group highlighted, click to jump (PreflightRunEngine.GoToGroup). At 100+ checks this
    /// is the main defence against the wizard losing all sense of place - see
    /// WP1_wizard_grouping_pagination_plan.md §5. Free jumping is safe: TryCompleteRun re-checks
    /// every check in the whole run independent of which pages were actually visited.
    /// </summary>
    public class GroupRailPanel : Panel
    {
        readonly FlowLayoutPanel _groups = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false
        };
        readonly Label _lblOverall = new Label { Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 14 };

        public event EventHandler<string> GroupClicked;

        public GroupRailPanel()
        {
            Width = 190;
            Dock = DockStyle.Left;
            Padding = new Padding(6);
            Controls.Add(_groups);
            Controls.Add(_progress);
            Controls.Add(_lblOverall);
        }

        public void Populate(IReadOnlyList<PreflightPage> pages, PreflightRun run, string currentGroupName)
        {
            _groups.Controls.Clear();

            var latest = run.LatestPerCheck.ToDictionary(r => r.CheckId, r => r, StringComparer.OrdinalIgnoreCase);
            var groupNames = pages.Select(p => p.GroupName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var totalChecks = 0;
            var answeredChecks = 0;

            foreach (var groupName in groupNames)
            {
                var groupChecks = pages
                    .Where(p => string.Equals(p.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Checks)
                    .ToList();

                var answered = groupChecks.Count(c => latest.ContainsKey(c.Id));
                var hasFail = groupChecks.Any(c => latest.TryGetValue(c.Id, out var r) && r.Outcome == CheckOutcome.Fail);

                totalChecks += groupChecks.Count;
                answeredChecks += answered;

                var isCurrent = string.Equals(groupName, currentGroupName, StringComparison.OrdinalIgnoreCase);
                var prefix = isCurrent ? "▶ " : (answered == groupChecks.Count ? "✓ " : "  ");
                var suffix = hasFail ? "  ⚠" : "";

                var link = new LinkLabel
                {
                    Text = $"{prefix}{groupName} {answered}/{groupChecks.Count}{suffix}",
                    AutoSize = true,
                    // SystemColors.HotTrack/DarkRed read fine on a light background but are hard to
                    // see against the app's dark theme - match CheckRowPanel's fix: the app's own
                    // button green for the normal case, a bright red when the group has a fail.
                    LinkColor = hasFail ? Color.Red : ThemeManager.ButBG,
                    ActiveLinkColor = hasFail ? Color.Red : ThemeManager.ButBG,
                    VisitedLinkColor = hasFail ? Color.Red : ThemeManager.ButBG,
                    Font = isCurrent
                        ? new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)
                        : new Font(FontFamily.GenericSansSerif, 9)
                };
                link.LinkClicked += (s, e) => GroupClicked?.Invoke(this, groupName);
                _groups.Controls.Add(link);
            }

            _lblOverall.Text = $"{answeredChecks} of {totalChecks}";
            _progress.Maximum = Math.Max(1, totalChecks);
            _progress.Value = Math.Min(answeredChecks, _progress.Maximum);
        }
    }
}
