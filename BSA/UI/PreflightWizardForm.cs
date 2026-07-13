using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Reports;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// One check per step: instruction/evidence, Pass/Fail/N-A + notes, progress, abort. Reaching the
    /// last step transitions to a sign-off summary (PreflightSignOffPanel). Every path out of this form
    /// (Sign Off, Abort button, or closing the window mid-run) writes a report - see WP1's "every run,
    /// including aborted, saves a report" acceptance criterion. A report-write failure blocks a Go result
    /// from ever being published, even though CompleteRun() already computed it internally - see
    /// EnsureFinished().
    /// </summary>
    public class PreflightWizardForm : Form
    {
        readonly PreflightRunEngine _engine;
        readonly Label _lblHeader = new Label();
        readonly Label _lblOperator = new Label();
        readonly Panel _pnlContent = new Panel();
        readonly Button _btnBack = new Button { Text = "< Back", AutoSize = true };
        readonly Button _btnNext = new Button { Text = "Next >", AutoSize = true };
        readonly Button _btnAbort = new Button { Text = "Abort", AutoSize = true };
        readonly CheckStepPanel _stepPanel = new CheckStepPanel();
        readonly PreflightSignOffPanel _signOffPanel = new PreflightSignOffPanel();

        // WinForms Timer (UI-thread Tick, no Invoke needed) - same pattern as
        // Controls.PreFlight.CheckListControl's live-update timer.
        readonly Timer _linkWatchdog = new Timer { Interval = 1000 };
        readonly Func<bool> _linkProbe;
        readonly string _reportsDirectory;
        bool _linkSeenUp;

        bool _reportWritten;

        /// <param name="linkProbe">Returns whether the MAVLink connection is currently open. Defaults
        /// to MainV2.comPort.BaseStream.IsOpen (the codebase's standard connected check); injectable so
        /// the link-loss abort path is unit-testable without a real connection.</param>
        /// <param name="reportsDirectory">Defaults to BsaPaths.ReportsDirectory; injectable so tests
        /// don't write reports into the real user data folder.</param>
        public PreflightWizardForm(PreflightRunEngine engine, Func<bool> linkProbe = null, string reportsDirectory = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _linkProbe = linkProbe ?? DefaultLinkProbe;
            _reportsDirectory = reportsDirectory ?? BsaPaths.ReportsDirectory;

            Text = "BSA Preflight";
            Width = 720;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 420);

            _lblHeader.Dock = DockStyle.Top;
            _lblHeader.Height = 28;
            _lblHeader.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            _lblHeader.Padding = new Padding(8, 6, 8, 0);

            _lblOperator.Dock = DockStyle.Top;
            _lblOperator.Height = 20;
            _lblOperator.Padding = new Padding(8, 0, 0, 0);
            _lblOperator.Text = $"Operator: {engine.Run.OperatorName}    Run: {engine.Run.RunId}";

            _pnlContent.Dock = DockStyle.Fill;

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            _btnNext.Click += (s, e) => OnNextClicked();
            _btnBack.Click += (s, e) => OnBackClicked();
            _btnAbort.Click += (s, e) => OnAbortClicked();
            buttonRow.Controls.Add(_btnNext);
            buttonRow.Controls.Add(_btnBack);
            buttonRow.Controls.Add(_btnAbort);

            Controls.Add(_pnlContent);
            Controls.Add(buttonRow);
            Controls.Add(_lblOperator);
            Controls.Add(_lblHeader);

            _stepPanel.AnswerChanged += (s, e) => UpdateNextEnabled();
            _signOffPanel.SignOffClicked += (s, e) => OnSignOffClicked();

            FormClosing += OnFormClosing;

            _linkWatchdog.Tick += (s, e) => PollLink();
            _linkWatchdog.Start();
            // Disposed (not FormClosed) - FormClosed never fires for a form whose handle was never
            // created, which would leak the started timer in headless/test usage.
            Disposed += (s, e) => _linkWatchdog.Dispose();

            ShowCurrentStep();
        }

        static bool DefaultLinkProbe()
        {
            try
            {
                return MainV2.comPort?.BaseStream?.IsOpen == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The third abort trigger alongside the Abort button and FormClosing: a MAVLink connection
        /// that was up during this run and then dropped aborts the run (report still written, result
        /// UNKNOWN). A run that never had a link (bench/offline walkaround) is deliberately not aborted
        /// - there is no connection to lose. IsOpen catches explicit disconnects and closed ports;
        /// silent telemetry loss on connectionless links (UDP) surfaces through the auto checks'
        /// link-quality evaluation instead. Called by the watchdog timer; public so the abort path is
        /// directly drivable in unit tests (same convention as MissionSanityChecks' public evaluators).
        /// </summary>
        public void PollLink()
        {
            if (_linkProbe())
            {
                _linkSeenUp = true;
                return;
            }

            if (!_linkSeenUp)
                return;

            if (_engine.Run.State != PreflightRunState.InProgress && _engine.Run.State != PreflightRunState.AwaitingSignOff)
                return;

            _linkWatchdog.Stop();
            _engine.Abort("MAVLink connection lost mid-run.");
            EnsureFinished(isAbort: true);
            try
            {
                CustomMessageBox.Show(
                    "Telemetry link lost - the preflight run was aborted and saved with result UNKNOWN.",
                    "BSA Preflight");
            }
            catch
            {
                // CustomMessageBox.Show throws when no UI handler is wired (headless/test context).
                // The abort and report write above must stand regardless - swallow only the display.
            }
            Close();
        }

        void ShowCurrentStep()
        {
            var check = _engine.CurrentCheck;
            if (check == null)
            {
                ShowSignOffPanel();
                return;
            }

            _pnlContent.Controls.Clear();
            _pnlContent.Controls.Add(_stepPanel);

            var suggestion = (outcome: CheckOutcome.Unknown, detail: (string)null);
            if (check.Type != CheckType.Manual)
                suggestion = _engine.EvaluateCurrentCheck();

            // If this step already has a recorded answer (operator navigated Back to review it), that
            // answer must win over a freshly recomputed suggestion - see CheckStepPanel.Populate's doc
            // comment for why (silent notes loss / silent override reversal otherwise).
            var priorAnswer = _engine.Run.LatestPerCheck.FirstOrDefault(r => r.CheckId == check.Id);
            _stepPanel.Populate(check, suggestion.outcome, suggestion.detail, priorAnswer);

            _lblHeader.Text = $"Step {_engine.Run.CurrentStepIndex + 1} of {_engine.Run.Checks.Count}: {check.Title}";
            _btnBack.Enabled = _engine.CanGoPrevious;
            _btnNext.Visible = true;

            if (check.Type == CheckType.Auto)
            {
                // Nothing for the operator to decide - record the auto-evaluated outcome immediately so
                // Next() (which requires an answer) works without operator interaction, matching "Auto:
                // fully automatic, no operator gate."
                RecordAutoResult(suggestion.outcome, suggestion.detail);
            }

            UpdateNextEnabled();
        }

        void RecordAutoResult(CheckOutcome outcome, string detail)
        {
            try
            {
                _engine.RecordResult(outcome, detail: detail);
            }
            catch (InvalidOperationException)
            {
                // RequiresNoteOnFail on an Auto check with no operator present to supply one - record
                // Unknown with an explanatory note rather than leaving the step unanswered and the
                // wizard stuck (Next() requires an answer to proceed).
                _engine.RecordResult(CheckOutcome.Unknown,
                    notes: "Auto check failed and required a note; none available without operator input.",
                    detail: detail);
            }
        }

        void UpdateNextEnabled()
        {
            var check = _engine.CurrentCheck;
            _btnNext.Enabled = check == null || check.Type == CheckType.Auto || _stepPanel.TryGetAnswer(out _, out _);
        }

        void OnNextClicked()
        {
            var check = _engine.CurrentCheck;
            if (check != null && check.Type != CheckType.Auto)
            {
                if (!_stepPanel.TryGetAnswer(out var outcome, out var notes))
                    return;

                try
                {
                    _engine.RecordResult(outcome, notes);
                }
                catch (InvalidOperationException ex)
                {
                    CustomMessageBox.Show(ex.Message, "Cannot continue");
                    return;
                }
            }

            _engine.Next();
            ShowCurrentStep();
        }

        void OnBackClicked()
        {
            _engine.Previous();
            ShowCurrentStep();
        }

        void ShowSignOffPanel()
        {
            _pnlContent.Controls.Clear();
            _pnlContent.Controls.Add(_signOffPanel);
            _signOffPanel.Populate(_engine.Run);

            _lblHeader.Text = "Final sign-off";
            _btnBack.Enabled = true;
            _btnNext.Visible = false;
        }

        void OnSignOffClicked()
        {
            _engine.CompleteRun();
            EnsureFinished(isAbort: false);
            Close();
        }

        void OnAbortClicked()
        {
            if (CustomMessageBox.Show("Abort this preflight run? A report will still be saved with result UNKNOWN.",
                    "Abort preflight", CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
                return;

            // The link watchdog ticks during modal message loops and can abort + close this form while
            // the confirm dialog above is open - in that case the run is already handled, and touching
            // the disposed form would throw.
            if (IsDisposed)
                return;

            _engine.Abort("Operator clicked Abort.");
            EnsureFinished(isAbort: true);
            Close();
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _linkWatchdog.Stop();

            if (_engine.Run.State == PreflightRunState.InProgress || _engine.Run.State == PreflightRunState.AwaitingSignOff)
                _engine.Abort("Wizard window closed mid-run.");

            // Whether we just aborted it here, or it's already terminal because a button handler is
            // mid-way through its own Close() call, EnsureFinished() is idempotent - safe either way.
            EnsureFinished(isAbort: _engine.Run.State == PreflightRunState.Aborted);
        }

        /// <summary>
        /// Writes the run's report and, only on a successful write, publishes the result through
        /// BsaPreflightService - a failed write must never let a Go/Warning result be observed by a
        /// future WP3 listener. An aborted run's Unknown result is always published even if the write
        /// fails (there is no Go to protect), but the operator is still shown the write failure since the
        /// audit trail is the entire point of this feature. Idempotent - safe to call more than once
        /// (button handler followed by FormClosing).
        /// </summary>
        void EnsureFinished(bool isAbort)
        {
            if (_reportWritten)
                return;

            // The report must never be lost because vehicle identity happens to be unreadable
            // (disconnected mid-run, headless test context) - identity fields degrade to null instead.
            byte? sysid = null;
            string frameString = null;
            try
            {
                sysid = MainV2.comPort?.MAV?.sysid;
                frameString = MainV2.comPort?.MAV?.FrameString;
            }
            catch
            {
            }

            Exception writeError = null;
            try
            {
                var report = PreflightReportWriter.BuildReport(
                    _engine.Run,
                    Application.ProductVersion,
                    preflightConfigHash: BsaHash.HashObject(_engine.Run.Checks),
                    mpConfigHash: BsaConfigComposition.ComputeLiveConfigHash(),
                    sysid: sysid,
                    frameString: frameString);

                PreflightReportWriter.Write(report, _reportsDirectory);
                _reportWritten = true;
            }
            catch (Exception ex)
            {
                writeError = ex;
            }

            if (writeError != null)
            {
                CustomMessageBox.Show(
                    "The preflight report could not be saved:\n" + writeError.Message +
                    (isAbort ? "" : "\n\nThis run will NOT be published as GO until a report is saved."),
                    "Report save failed");
            }

            if (isAbort || _reportWritten)
                BsaPreflightService.Instance.PublishResult(_engine);
        }
    }
}
