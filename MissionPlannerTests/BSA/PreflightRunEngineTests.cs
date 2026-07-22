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
        static PreflightCheckDefinition Manual(string id, bool allowNa = true, bool requiresNoteOnFail = false, string group = null) =>
            new PreflightCheckDefinition
            {
                Id = id,
                Title = id,
                Type = CheckType.Manual,
                Severity = CheckSeverity.Critical,
                Instruction = "do it",
                AllowNotApplicable = allowNa,
                RequiresNoteOnFail = requiresNoteOnFail,
                Group = group
            };

        // PageSize 1 keeps this helper's tests behaving exactly like the pre-grouping suite (one
        // check per page/step) - they're exercising run/navigation state machinery, not pagination
        // itself. Pagination-specific behavior gets its own tests further down with explicit metadata.
        static PreflightRunEngine NewEngine(params PreflightCheckDefinition[] checks) =>
            NewEngine(new RegisteredCheckRegistry(), new PreflightChecklistMetadata { PageSize = 1 }, checks);

        static PreflightRunEngine NewEngine(RegisteredCheckRegistry registry, PreflightChecklistMetadata metadata,
            params PreflightCheckDefinition[] checks)
        {
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            return new PreflightRunEngine(checks, evaluator, registry, "Test Operator", metadata: metadata);
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
            engine.RecordResult("c1", CheckOutcome.Pass);
            engine.Next();
            Assert.AreEqual("c2", engine.CurrentCheck.Id);
        }

        [TestMethod]
        public void Next_PastLastCheck_ReachesAwaitingSignOff()
        {
            var engine = NewEngine(Manual("c1"));
            engine.RecordResult("c1", CheckOutcome.Pass);
            engine.Next();
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State);
            Assert.IsNull(engine.CurrentCheck);
        }

        [TestMethod]
        public void RecordResult_NotApplicable_WhenNotAllowed_Throws()
        {
            var engine = NewEngine(Manual("c1", allowNa: false));
            Assert.ThrowsException<InvalidOperationException>(() => engine.RecordResult("c1", CheckOutcome.NotApplicable));
        }

        [TestMethod]
        public void RecordResult_UnknownCheckId_Throws()
        {
            var engine = NewEngine(Manual("c1"));
            Assert.ThrowsException<InvalidOperationException>(() => engine.RecordResult("does-not-exist", CheckOutcome.Pass));
        }

        [TestMethod]
        public void RecordResult_FailWithoutRequiredNote_ReturnsFalse_HeldUnrecorded()
        {
            // Not a throw: the UI calls RecordResult reactively on every answer change, and clicking
            // FAIL before typing a note is a normal interactive state, not a caller bug (see
            // PreflightRunEngine.RecordResult's doc comment).
            var engine = NewEngine(Manual("c1", requiresNoteOnFail: true));
            var recorded = engine.RecordResult("c1", CheckOutcome.Fail);

            Assert.IsFalse(recorded);
            Assert.AreEqual(0, engine.Run.History.Count);
        }

        [TestMethod]
        public void RecordResult_FailWithNote_Succeeds()
        {
            var engine = NewEngine(Manual("c1", requiresNoteOnFail: true));
            var recorded = engine.RecordResult("c1", CheckOutcome.Fail, notes: "explanation");
            Assert.IsTrue(recorded);
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
            engine.RecordResult("c1", CheckOutcome.Pass);
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
            engine.RecordResult("c1", CheckOutcome.Fail, notes: "first answer");
            engine.RecordResult("c1", CheckOutcome.Pass, notes: "changed my mind");

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
            engine.RecordResult("c1", CheckOutcome.Pass);
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
            engine.RecordResult("c1", CheckOutcome.Pass);
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
            engine.RecordResult("c1", CheckOutcome.Fail); // a critical fail, if it ever aggregated, would be NoGo
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
            engine.RecordResult("c1", CheckOutcome.Pass);
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
            engine.RecordResult("c1", CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();

            service.PublishResult(engine);

            Assert.AreEqual(PreflightResult.Go, observed);
            Assert.AreEqual(PreflightResult.Go, service.Current);
        }

        // ---- Grouping/pagination navigation (WP1_wizard_grouping_pagination_plan.md §3) ----

        static PreflightChecklistMetadata GroupedMetadata(int pageSize = 5) => new PreflightChecklistMetadata
        {
            Groups = new List<string> { "A", "B" },
            PageSize = pageSize,
            AutoChecksFirst = false
        };

        [TestMethod]
        public void TryAdvance_PartialPage_ReturnsFalse_NamesExactlyTheMissingChecks()
        {
            var engine = NewEngine(new RegisteredCheckRegistry(), GroupedMetadata(),
                Manual("a1", group: "A"), Manual("a2", group: "A"), Manual("a3", group: "A"));

            engine.RecordResult("a1", CheckOutcome.Pass);
            // a2, a3 left unanswered.

            var advanced = engine.TryAdvance(out var unanswered);

            Assert.IsFalse(advanced);
            CollectionAssert.AreEquivalent(new[] { "a2", "a3" }, unanswered.ToList());
            Assert.AreEqual("a1", engine.CurrentPage.Checks[0].Id); // did not move
        }

        [TestMethod]
        public void TryAdvance_FullyAnsweredPage_Advances()
        {
            var engine = NewEngine(new RegisteredCheckRegistry(), GroupedMetadata(),
                Manual("a1", group: "A"), Manual("b1", group: "B"));

            engine.RecordResult("a1", CheckOutcome.Pass);
            var advanced = engine.TryAdvance(out var unanswered);

            Assert.IsTrue(advanced);
            Assert.AreEqual(0, unanswered.Count);
            Assert.AreEqual("b1", engine.CurrentPage.Checks[0].Id);
        }

        [TestMethod]
        public void GoToGroup_JumpsForward_AnswersOnOriginalPageSurvive()
        {
            var engine = NewEngine(new RegisteredCheckRegistry(), GroupedMetadata(),
                Manual("a1", group: "A"), Manual("b1", group: "B"));

            engine.RecordResult("a1", CheckOutcome.Pass);
            var jumped = engine.GoToGroup("B");

            Assert.IsTrue(jumped);
            Assert.AreEqual("b1", engine.CurrentPage.Checks[0].Id);

            engine.GoToGroup("A"); // jump back
            Assert.AreEqual("a1", engine.CurrentPage.Checks[0].Id);
            Assert.AreEqual(CheckOutcome.Pass, engine.Run.LatestPerCheck.First(r => r.CheckId == "a1").Outcome);
        }

        [TestMethod]
        public void GoToGroup_UnknownGroup_ReturnsFalse_DoesNotMove()
        {
            var engine = NewEngine(new RegisteredCheckRegistry(), GroupedMetadata(),
                Manual("a1", group: "A"), Manual("b1", group: "B"));

            var jumped = engine.GoToGroup("NoSuchGroup");

            Assert.IsFalse(jumped);
            Assert.AreEqual("a1", engine.CurrentPage.Checks[0].Id);
        }

        [TestMethod]
        public void JumpRailSkipsAPage_TryCompleteRun_CatchesTheUnansweredCheck()
        {
            // The jump rail can reach the last page without visiting every earlier one - sign-off must
            // catch that independently of per-page navigation (see TryCompleteRun's doc comment).
            var engine = NewEngine(new RegisteredCheckRegistry(), GroupedMetadata(),
                Manual("a1", group: "A"), Manual("b1", group: "B"));

            engine.GoToGroup("B"); // skip group A entirely
            engine.RecordResult("b1", CheckOutcome.Pass);
            engine.Next(); // last page -> AwaitingSignOff

            var outcome = engine.TryCompleteRun();

            Assert.IsFalse(outcome.Completed);
            CollectionAssert.Contains(outcome.UnansweredCheckIds.ToList(), "a1");
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State);
        }

        // ---- Auto re-verification at sign-off (WP1_wizard_grouping_pagination_plan.md §4/§4a/§4b) ----

        static PreflightCheckDefinition AutoCheck(string id, bool deferredToSignOff = false,
            bool requiresNoteOnFail = false) => new PreflightCheckDefinition
        {
            Id = id,
            Title = id,
            Type = CheckType.Auto,
            Severity = CheckSeverity.Critical,
            Source = CheckSource.Mission,
            Check = id,
            DeferredToSignOff = deferredToSignOff,
            RequiresNoteOnFail = requiresNoteOnFail
        };

        static RegisteredCheckRegistry RegistryWithSwitchableCheck(string key, Func<CheckOutcome> evaluate)
        {
            var registry = new RegisteredCheckRegistry();
            registry.Register(new DelegateRegisteredCheck(key, check => (evaluate(), "detail for " + key)));
            return registry;
        }

        [TestMethod]
        public void ReVerifyAtSignOff_MissionChangedMidRun_FlipsPassToFail_AggregatesNoGo()
        {
            // The regression this whole feature exists for: a mission-unchanged-style check must get
            // its real answer at sign-off, not stay frozen at whatever it evaluated to on page 1.
            var stillUnchanged = true;
            var registry = RegistryWithSwitchableCheck("mission.unchanged",
                () => stillUnchanged ? CheckOutcome.Pass : CheckOutcome.Fail);
            var engine = NewEngine(registry, null, AutoCheck("mission.unchanged"));

            Assert.AreEqual(CheckOutcome.Pass, engine.Run.LatestPerCheck.First().Outcome); // AutoInitial

            stillUnchanged = false; // "mission edited during the walkaround"
            engine.Next(); // last (only) page -> AwaitingSignOff triggers re-verify

            Assert.AreEqual(CheckOutcome.Fail, engine.Run.LatestPerCheck.First().Outcome);
            Assert.IsTrue(engine.Run.HasAutoReverifyChange("mission.unchanged"));

            // Stable at Fail from here (no further change before the click) - Sign Off succeeds, and
            // the mid-run edit shows up in the aggregated result, not as a §4a refusal (that's the
            // separate "changed again after AwaitingSignOff" scenario - see
            // TryCompleteRun_RefusesOnLateChange_StaysAwaitingSignOff_NothingPublishedYet).
            var outcome = engine.TryCompleteRun();
            Assert.IsTrue(outcome.Completed);
            Assert.AreEqual(PreflightResult.NoGo, engine.Run.Result);
        }

        [TestMethod]
        public void ReVerifyAtSignOffClick_NothingChanged_CompletesAndAggregatesNoGo()
        {
            var registry = RegistryWithSwitchableCheck("mission.unchanged", () => CheckOutcome.Fail);
            var engine = NewEngine(registry, null, AutoCheck("mission.unchanged"));

            engine.Next(); // AwaitingSignOff: re-verify records Fail (same as AutoInitial - stable)
            var outcome = engine.TryCompleteRun(); // re-verify again: still Fail, no change -> proceeds

            Assert.IsTrue(outcome.Completed);
            Assert.AreEqual(PreflightRunState.Completed, engine.Run.State);
            Assert.AreEqual(PreflightResult.NoGo, engine.Run.Result); // Critical Fail
        }

        [TestMethod]
        public void TryCompleteRun_RefusesOnLateChange_StaysAwaitingSignOff_NothingPublishedYet()
        {
            var stable = true;
            var registry = RegistryWithSwitchableCheck("mission.unchanged", () => stable ? CheckOutcome.Pass : CheckOutcome.Fail);
            var engine = NewEngine(registry, null, AutoCheck("mission.unchanged"));

            engine.Next(); // AwaitingSignOff: re-verify records Pass
            stable = false; // changes again, after the sign-off page was already shown

            var outcome = engine.TryCompleteRun();

            Assert.IsFalse(outcome.Completed);
            Assert.AreEqual(1, outcome.ChangedAutoChecks.Count);
            Assert.AreEqual("mission.unchanged", outcome.ChangedAutoChecks[0].CheckId);
            Assert.AreEqual(CheckOutcome.Pass, outcome.ChangedAutoChecks[0].Before);
            Assert.AreEqual(CheckOutcome.Fail, outcome.ChangedAutoChecks[0].After);
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State); // not finalized
        }

        [TestMethod]
        public void TryCompleteRun_SecondAttemptAfterRefusal_WithNoFurtherChange_Completes()
        {
            var stable = true;
            var registry = RegistryWithSwitchableCheck("mission.unchanged", () => stable ? CheckOutcome.Pass : CheckOutcome.Fail);
            var engine = NewEngine(registry, null, AutoCheck("mission.unchanged"));

            engine.Next();
            stable = false;
            var refused = engine.TryCompleteRun();
            Assert.IsFalse(refused.Completed);

            var second = engine.TryCompleteRun(); // value is now stable at Fail - no further change
            Assert.IsTrue(second.Completed);
            Assert.AreEqual(PreflightRunState.Completed, engine.Run.State);
        }

        [TestMethod]
        public void SemiCheck_OperatorOverride_SurvivesSignOffReverification()
        {
            var engine = NewEngine(Manual("m1"), Semi("semi1"));
            engine.RecordResult("m1", CheckOutcome.Pass);
            engine.RecordResult("semi1", CheckOutcome.Pass, notes: "operator override, verified in person");

            engine.Next(); // past m1's page (this helper defaults to PageSize 1, one check per page)
            engine.Next(); // past semi1's page -> AwaitingSignOff (Semi is never re-verified, only Auto is)
            var outcome = engine.TryCompleteRun();

            Assert.IsTrue(outcome.Completed);
            var latest = engine.Run.LatestPerCheck.First(r => r.CheckId == "semi1");
            Assert.AreEqual(CheckOutcome.Pass, latest.Outcome);
            Assert.AreEqual("operator override, verified in person", latest.Notes);
        }

        static PreflightCheckDefinition Semi(string id) => new PreflightCheckDefinition
        {
            Id = id,
            Title = id,
            Type = CheckType.Semi,
            Severity = CheckSeverity.Warning,
            Instruction = "confirm",
            Source = CheckSource.Telemetry,
            Field = "x",
            Condition = CheckCondition.GTEQ,
            Value = 1,
            AllowNotApplicable = false
        };

        [TestMethod]
        public void DeferredToSignOff_InitialState_RecordsUnknownWithExplanation_DoesNotBlockAdvance()
        {
            var registry = RegistryWithSwitchableCheck("mission.unchanged", () => CheckOutcome.Pass);
            var engine = NewEngine(registry, null, AutoCheck("mission.unchanged", deferredToSignOff: true));

            var initial = engine.Run.LatestPerCheck.First();
            Assert.AreEqual(CheckOutcome.Unknown, initial.Outcome);
            StringAssert.Contains(initial.Detail, "sign-off");

            // Must not block reaching AwaitingSignOff even though it "answered" Unknown.
            engine.Next();
            Assert.AreEqual(PreflightRunState.AwaitingSignOff, engine.Run.State);

            // Re-verification at AwaitingSignOff-entry gives its real answer.
            var reverified = engine.Run.LatestPerCheck.First();
            Assert.AreEqual(CheckOutcome.Pass, reverified.Outcome);
        }
    }
}
