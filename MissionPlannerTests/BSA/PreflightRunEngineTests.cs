using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class PreflightRunEngineTests
    {
        static PreflightCheckDefinition Manual(string id, bool allowNa = true, bool requiresNoteOnFail = false) =>
            new PreflightCheckDefinition
            {
                Id = id,
                Title = id,
                Type = CheckType.Manual,
                Severity = CheckSeverity.Critical,
                Instruction = "do it",
                AllowNotApplicable = allowNa,
                RequiresNoteOnFail = requiresNoteOnFail
            };

        static PreflightRunEngine NewEngine(params PreflightCheckDefinition[] checks)
        {
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            var registry = new RegisteredCheckRegistry();
            return new PreflightRunEngine(checks, evaluator, registry, "Test Operator");
        }

        [TestMethod]
        public void NewRun_StartsInProgress_AtFirstCheck()
        {
            var engine = NewEngine(Manual("c1"), Manual("c2"));
            Assert.AreEqual(PreflightRunState.InProgress, engine.Run.State);
            Assert.AreEqual("c1", engine.CurrentCheck.Id);
        }

        [TestMethod]
        public void Next_WithoutAnswering_Throws()
        {
            var engine = NewEngine(Manual("c1"), Manual("c2"));
            Assert.ThrowsException<InvalidOperationException>(() => engine.Next());
        }

        [TestMethod]
        public void Next_AfterAnswering_Advances()
        {
            var engine = NewEngine(Manual("c1"), Manual("c2"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            Assert.AreEqual("c2", engine.CurrentCheck.Id);
        }

        [TestMethod]
        public void Next_PastLastCheck_ReachesAwaitingSignOff()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State);
            Assert.IsNull(engine.CurrentCheck);
        }

        [TestMethod]
        public void RecordResult_NotApplicable_WhenNotAllowed_Throws()
        {
            var engine = NewEngine(Manual("c1", allowNa: false));
            Assert.ThrowsException<InvalidOperationException>(() => engine.RecordResult(CheckOutcome.NotApplicable));
        }

        [TestMethod]
        public void RecordResult_FailWithoutRequiredNote_Throws()
        {
            var engine = NewEngine(Manual("c1", requiresNoteOnFail: true));
            Assert.ThrowsException<InvalidOperationException>(() => engine.RecordResult(CheckOutcome.Fail));
        }

        [TestMethod]
        public void RecordResult_FailWithNote_Succeeds()
        {
            var engine = NewEngine(Manual("c1", requiresNoteOnFail: true));
            engine.RecordResult(CheckOutcome.Fail, notes: "explanation");
            Assert.AreEqual(1, engine.Run.History.Count);
        }

        [TestMethod]
        public void Previous_FromFirstCheck_DoesNothing()
        {
            var engine = NewEngine(Manual("c1"), Manual("c2"));
            engine.Previous();
            Assert.AreEqual("c1", engine.CurrentCheck.Id);
        }

        [TestMethod]
        public void Previous_FromAwaitingSignOff_ReopensLastStep()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State);

            engine.Previous();
            Assert.AreEqual(PreflightRunState.InProgress, engine.Run.State);
            Assert.AreEqual("c1", engine.CurrentCheck.Id);
        }

        [TestMethod]
        public void ReAnswering_AppendsToHistory_LatestWins()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Fail, notes: "first answer");
            engine.RecordResult(CheckOutcome.Pass, notes: "changed my mind");

            Assert.AreEqual(2, engine.Run.History.Count);
            Assert.IsTrue(engine.Run.HasChangedAnswer("c1"));
            var latest = engine.Run.LatestPerCheck.Single();
            Assert.AreEqual(CheckOutcome.Pass, latest.Outcome);
        }

        [TestMethod]
        public void CompleteRun_BeforeAwaitingSignOff_Throws()
        {
            var engine = NewEngine(Manual("c1"));
            Assert.ThrowsException<InvalidOperationException>(() => engine.CompleteRun());
        }

        [TestMethod]
        public void CompleteRun_AfterAllAnswered_SetsResult()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();

            Assert.AreEqual(PreflightRunState.Completed, engine.Run.State);
            Assert.AreEqual(PreflightResult.Go, engine.Run.Result);
            Assert.IsNotNull(engine.Run.EndedUtc);
        }

        [TestMethod]
        public void CompleteRun_IsIdempotent()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();
            var endedAt = engine.Run.EndedUtc;

            engine.CompleteRun(); // must not throw or change state
            Assert.AreEqual(endedAt, engine.Run.EndedUtc);
        }

        [TestMethod]
        public void Abort_SetsUnknownResult_RegardlessOfPriorAnswers()
        {
            var engine = NewEngine(Manual("c1", requiresNoteOnFail: false), Manual("c2"));
            engine.RecordResult(CheckOutcome.Fail); // a critical fail, if it ever aggregated, would be NoGo
            engine.Abort("test abort");

            Assert.AreEqual(PreflightRunState.Aborted, engine.Run.State);
            Assert.AreEqual(PreflightResult.Unknown, engine.Run.Result);
            Assert.AreEqual("test abort", engine.Run.AbortReason);
        }

        [TestMethod]
        public void Abort_IsIdempotent_SecondCallIsNoOp()
        {
            var engine = NewEngine(Manual("c1"));
            engine.Abort("first");
            var endedAt = engine.Run.EndedUtc;

            engine.Abort("second"); // must not throw, must not overwrite the original reason
            Assert.AreEqual("first", engine.Run.AbortReason);
            Assert.AreEqual(endedAt, engine.Run.EndedUtc);
        }

        [TestMethod]
        public void Abort_AfterCompleted_IsNoOp()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();

            engine.Abort("too late");
            Assert.AreEqual(PreflightRunState.Completed, engine.Run.State);
            Assert.AreEqual(PreflightResult.Go, engine.Run.Result);
        }

        [TestMethod]
        public void EmptyChecklist_RejectedAtConstruction()
        {
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            var registry = new RegisteredCheckRegistry();
            Assert.ThrowsException<ArgumentException>(() =>
                new PreflightRunEngine(new List<PreflightCheckDefinition>(), evaluator, registry, "Test Operator"));
        }

        [TestMethod]
        public void StatusChanged_FiresOnPublish_WithFinalResult()
        {
            // WP3 handshake stub: a fresh (non-singleton) service instance so this test doesn't touch
            // BsaPreflightService.Instance or leak state into other tests.
            var service = new BsaPreflightService();
            PreflightResult? observed = null;
            service.StatusChanged += (s, e) => observed = e.Result;

            var engine = NewEngine(Manual("c1"));
            engine.RecordResult(CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();

            service.PublishResult(engine);

            Assert.AreEqual(PreflightResult.Go, observed);
            Assert.AreEqual(PreflightResult.Go, service.Current);
        }
    }
}
