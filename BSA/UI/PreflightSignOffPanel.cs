using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Final step: summary of every answer plus a live-preview verdict (via PreflightAggregator, as if
    /// signing off right now), and a Sign Off button. Reachable via Back too - see
    /// PreflightRunEngine.Previous() re-opening the last step for review before a real sign-off.
    /// </summary>
    public class PreflightSignOffPanel : Panel
    {
        readonly Label _lblSummary = new Label();
        readonly ListBox _lstIssues = new ListBox();
        readonly Button _btnSignOff = new Button { Text = "Sign Off", AutoSize = true };

        public event EventHandler SignOffClicked;

        public PreflightSignOffPanel()
        {
            Dock = DockStyle.Fill;

            _lblSummary.AutoSize = true;
            _lblSummary.MaximumSize = new Size(560, 0);
            _lblSummary.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
            _lstIssues.Width = 500;
            _lstIssues.Height = 220;
            _btnSignOff.Click += (s, e) => SignOffClicked?.Invoke(this, EventArgs.Empty);

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16)
            };
            flow.Controls.Add(_lblSummary);
            flow.Controls.Add(new Panel { Height = 12, Width = 1 });
            flow.Controls.Add(_lstIssues);
            flow.Controls.Add(new Panel { Height = 12, Width = 1 });
            flow.Controls.Add(_btnSignOff);
            Controls.Add(flow);
        }

        public void Populate(PreflightRun run)
        {
            var preview = PreflightAggregator.Aggregate(run.LatestPerCheck, signOffCompleted: true);
            _lblSummary.Text = $"Ready to sign off. If you sign off now, result: {preview}";

            _lstIssues.Items.Clear();
            foreach (var r in run.LatestPerCheck.Where(r => r.Outcome == CheckOutcome.Fail || r.Outcome == CheckOutcome.Unknown))
                _lstIssues.Items.Add($"[{r.Severity}] {r.CheckTitle}: {r.Outcome}" +
                                      (string.IsNullOrEmpty(r.Notes) ? "" : $" - {r.Notes}"));

            foreach (var id in run.Checks.Select(c => c.Id).Where(run.HasChangedAnswer))
                _lstIssues.Items.Add($"(answer changed during this run: {id})");

            if (_lstIssues.Items.Count == 0)
                _lstIssues.Items.Add("No issues recorded.");
        }
    }
}
