using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class PreflightAggregatorTests
    {
        static CheckResultRecord Record(CheckSeverity severity, CheckOutcome outcome) => new CheckResultRecord
        {
            CheckId = Guid.NewGuid().ToString(),
            CheckTitle = "test",
            Severity = severity,
            Outcome = outcome,
            TimestampUtc = DateTime.UtcNow
        };

        [TestMethod]
        public void AllPass_SignedOff_IsGo()
        {
            var records = new[]
            {
                Record(CheckSeverity.Critical, CheckOutcome.Pass),
                Record(CheckSeverity.Warning, CheckOutcome.Pass)
            };
            Assert.AreEqual(PreflightResult.Go, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void CriticalFail_IsNoGo()
        {
            var records = new[] { Record(CheckSeverity.Critical, CheckOutcome.Fail) };
            Assert.AreEqual(PreflightResult.NoGo, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void CriticalUnknown_IsNoGo_SameAsCriticalFail()
        {
            var failRecords = new[] { Record(CheckSeverity.Critical, CheckOutcome.Fail) };
            var unknownRecords = new[] { Record(CheckSeverity.Critical, CheckOutcome.Unknown) };
            Assert.AreEqual(PreflightAggregator.Aggregate(failRecords, true), PreflightAggregator.Aggregate(unknownRecords, true));
            Assert.AreEqual(PreflightResult.NoGo, PreflightAggregator.Aggregate(unknownRecords, true));
        }

        [TestMethod]
        public void CriticalFail_IsNoGo_EvenWithoutSignOff()
        {
            // A critical blocker always wins - sign-off state doesn't change that once one exists.
            var records = new[] { Record(CheckSeverity.Critical, CheckOutcome.Fail) };
            Assert.AreEqual(PreflightResult.NoGo, PreflightAggregator.Aggregate(records, false));
        }

        [TestMethod]
        public void WarningFail_Alone_IsWarning_NotNoGo()
        {
            var records = new[]
            {
                Record(CheckSeverity.Critical, CheckOutcome.Pass),
                Record(CheckSeverity.Warning, CheckOutcome.Fail)
            };
            Assert.AreEqual(PreflightResult.Warning, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void InfoFail_Alone_IsGo_RecordedOnly()
        {
            var records = new[]
            {
                Record(CheckSeverity.Critical, CheckOutcome.Pass),
                Record(CheckSeverity.Info, CheckOutcome.Fail)
            };
            Assert.AreEqual(PreflightResult.Go, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void MixedCriticalAndWarningFail_NoGoWins()
        {
            var records = new[]
            {
                Record(CheckSeverity.Critical, CheckOutcome.Fail),
                Record(CheckSeverity.Warning, CheckOutcome.Fail)
            };
            Assert.AreEqual(PreflightResult.NoGo, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void NotApplicable_OnCriticalCheck_NeverBlocksGoByItself()
        {
            var records = new[] { Record(CheckSeverity.Critical, CheckOutcome.NotApplicable) };
            Assert.AreEqual(PreflightResult.Go, PreflightAggregator.Aggregate(records, true));
        }

        [TestMethod]
        public void NotSignedOff_NoBlockerYet_IsUnknown_NotGo()
        {
            // This is the aborted-run-with-no-failures-yet case: incomplete, so Unknown, never Go.
            var records = new[] { Record(CheckSeverity.Critical, CheckOutcome.Pass) };
            Assert.AreEqual(PreflightResult.Unknown, PreflightAggregator.Aggregate(records, false));
        }

        [TestMethod]
        public void EmptyRecords_SignedOff_IsGo()
        {
            // The loader rejects an empty checklist before a run can start - this pins the aggregator's
            // own behavior for the input shape, not a reachable real-run state.
            Assert.AreEqual(PreflightResult.Go, PreflightAggregator.Aggregate(new List<CheckResultRecord>(), true));
        }

        [TestMethod]
        public void NullRecords_TreatedAsEmpty()
        {
            Assert.AreEqual(PreflightResult.Go, PreflightAggregator.Aggregate(null, true));
        }
    }
}
