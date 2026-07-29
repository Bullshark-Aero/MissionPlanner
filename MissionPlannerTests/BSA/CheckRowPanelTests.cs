using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class CheckRowPanelTests
    {
        static PreflightCheckDefinition ManualCheck(bool allowNa = true) => new PreflightCheckDefinition
        {
            Id = "correct-aircraft",
            Title = "Correct aircraft connected",
            Type = CheckType.Manual,
            Severity = CheckSeverity.Critical,
            Instruction = "Confirm and note the aircraft id.",
            AllowNotApplicable = allowNa
        };

        static PreflightCheckDefinition SemiCheck() => new PreflightCheckDefinition
        {
            Id = "battery-status-visible",
            Title = "Battery status",
            Type = CheckType.Semi,
            Severity = CheckSeverity.Warning,
            Instruction = "Confirm battery status.",
            AllowNotApplicable = false
        };

        static CheckResultRecord Record(CheckOutcome outcome, string notes) => new CheckResultRecord
        {
            CheckId = "irrelevant-for-this-test",
            CheckTitle = "irrelevant",
            Severity = CheckSeverity.Critical,
            Outcome = outcome,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        };

        [TestMethod]
        public void PriorAnswer_RestoresNotesAndOutcome_OverridingFreshSuggestion()
        {
            var panel = new CheckRowPanel();
            var check = ManualCheck();
            var prior = Record(CheckOutcome.Pass, "BSA-001");

            // Manual checks never have a live suggestion (Unknown/null), but the assertion that matters
            // is that the prior answer - not a blank slate - is what TryGetAnswer reports afterward.
            panel.Populate(check, CheckOutcome.Unknown, null, prior);

            Assert.IsTrue(panel.TryGetAnswer(out var outcome, out var notes));
            Assert.AreEqual(CheckOutcome.Pass, outcome);
            Assert.AreEqual("BSA-001", notes);
        }

        [TestMethod]
        public void PriorAnswer_TakesPrecedenceOverFreshAutoSuggestion_ForSemiChecks()
        {
            var panel = new CheckRowPanel();
            var check = SemiCheck();
            // Operator previously overrode a Fail auto-suggestion to Pass with a justification.
            var prior = Record(CheckOutcome.Pass, "Backup pack installed, verified adequate.");

            // Re-entering the step now sees a fresh suggestion of Fail (e.g. battery drained further) -
            // the operator's recorded override must still win, not the new live reading.
            panel.Populate(check, CheckOutcome.Fail, "battery_remaining = 40 (need >= 50)", prior);

            Assert.IsTrue(panel.TryGetAnswer(out var outcome, out var notes));
            Assert.AreEqual(CheckOutcome.Pass, outcome);
            Assert.AreEqual("Backup pack installed, verified adequate.", notes);
        }

        [TestMethod]
        public void NoPriorAnswer_FirstVisit_FallsBackToFreshSuggestion()
        {
            var panel = new CheckRowPanel();
            var check = SemiCheck();

            panel.Populate(check, CheckOutcome.Pass, "battery_remaining = 90 (need >= 50)", null);

            Assert.IsTrue(panel.TryGetAnswer(out var outcome, out var notes));
            Assert.AreEqual(CheckOutcome.Pass, outcome);
            Assert.AreEqual(string.Empty, notes);
        }

        [TestMethod]
        public void PriorAnswer_NotApplicable_IsRestored()
        {
            var panel = new CheckRowPanel();
            var check = ManualCheck(allowNa: true);
            var prior = Record(CheckOutcome.NotApplicable, "N/A for this airframe.");

            panel.Populate(check, CheckOutcome.Unknown, null, prior);

            Assert.IsTrue(panel.TryGetAnswer(out var outcome, out var notes));
            Assert.AreEqual(CheckOutcome.NotApplicable, outcome);
            Assert.AreEqual("N/A for this airframe.", notes);
        }
    }
}
