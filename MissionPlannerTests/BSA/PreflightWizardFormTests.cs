using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// Covers the wizard's link-loss abort trigger (the third abort path alongside the Abort button and
    /// FormClosing - see the WP1 plan's "three abort triggers" design). Drives PollLink directly rather
    /// than waiting on the WinForms timer, which never ticks without a message pump.
    /// </summary>
    [TestClass]
    public class PreflightWizardFormTests
    {
        static PreflightRunEngine NewEngine()
        {
            var check = new PreflightCheckDefinition
            {
                Id = "c1",
                Title = "c1",
                Type = CheckType.Manual,
                Severity = CheckSeverity.Critical,
                Instruction = "do it"
            };
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            return new PreflightRunEngine(new[] { check }, evaluator, new RegisteredCheckRegistry(), "Test Operator");
        }

        static string NewTempReportsDir() =>
            Path.Combine(Path.GetTempPath(), "BsaWizardFormTests_" + Guid.NewGuid().ToString("N"));

        [TestMethod]
        public void LinkNeverUp_PollDoesNotAbort_OfflineUseIsAllowed()
        {
            var engine = NewEngine();
            var reportsDir = NewTempReportsDir();
            try
            {
                using (var form = new PreflightWizardForm(engine, linkProbe: () => false, reportsDirectory: reportsDir))
                {
                    form.PollLink();
                    form.PollLink();

                    // Assert before Dispose - disposing an open wizard runs its own FormClosing abort.
                    Assert.AreEqual(PreflightRunState.InProgress, engine.Run.State,
                        "A run that never had a link must not be aborted by the link watchdog.");
                    Assert.IsFalse(Directory.Exists(reportsDir) && Directory.GetFiles(reportsDir, "*.json").Length > 0);
                }
            }
            finally
            {
                if (Directory.Exists(reportsDir)) Directory.Delete(reportsDir, true);
            }
        }

        [TestMethod]
        public void LinkUpThenLost_AbortsRun_AndStillWritesUnknownReport()
        {
            var engine = NewEngine();
            var reportsDir = NewTempReportsDir();
            var linkUp = true;
            try
            {
                using (var form = new PreflightWizardForm(engine, linkProbe: () => linkUp, reportsDirectory: reportsDir))
                {
                    form.PollLink(); // link seen up
                    linkUp = false;
                    form.PollLink(); // link lost -> abort + report + close

                    Assert.AreEqual(PreflightRunState.Aborted, engine.Run.State);
                    Assert.AreEqual(PreflightResult.Unknown, engine.Run.Result);
                    StringAssert.Contains(engine.Run.AbortReason, "lost");
                    Assert.AreEqual(1, Directory.GetFiles(reportsDir, "*.json").Length,
                        "The link-loss abort must still write exactly one report (WP1: every run saves a report).");
                }
            }
            finally
            {
                if (Directory.Exists(reportsDir)) Directory.Delete(reportsDir, true);
            }
        }

        [TestMethod]
        public void LinkStaysUp_PollNeverAborts()
        {
            var engine = NewEngine();
            var reportsDir = NewTempReportsDir();
            try
            {
                using (var form = new PreflightWizardForm(engine, linkProbe: () => true, reportsDirectory: reportsDir))
                {
                    form.PollLink();
                    form.PollLink();
                    Assert.AreEqual(PreflightRunState.InProgress, engine.Run.State);
                }
            }
            finally
            {
                if (Directory.Exists(reportsDir)) Directory.Delete(reportsDir, true);
            }
        }

        [TestMethod]
        public void LinkLost_AfterRunAlreadyCompleted_IsNoOp()
        {
            var engine = NewEngine();
            var reportsDir = NewTempReportsDir();
            var linkUp = true;
            try
            {
                using (var form = new PreflightWizardForm(engine, linkProbe: () => linkUp, reportsDirectory: reportsDir))
                {
                    form.PollLink();
                    engine.RecordResult(CheckOutcome.Pass);
                    engine.Next();
                    engine.CompleteRun();

                    linkUp = false;
                    form.PollLink(); // must not touch a terminal run

                    Assert.AreEqual(PreflightRunState.Completed, engine.Run.State);
                    Assert.AreEqual(PreflightResult.Go, engine.Run.Result);
                }
            }
            finally
            {
                if (Directory.Exists(reportsDir)) Directory.Delete(reportsDir, true);
            }
        }
    }
}
