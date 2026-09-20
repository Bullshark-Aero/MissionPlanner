using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Telemetry;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class JudicarHealthEvaluatorTests
    {
        [TestMethod]
        public void Evaluate_AppliesGraceFreshnessFiniteAndGpsRules()
        {
            var evaluator = new JudicarHealthEvaluator(
                Judicar2600BundleProfile.Create(new BsaQuickViewProfile
                {
                    Rows = 1, Columns = 1,
                    Cells = { new BsaQuickViewCell { Position = 1, SourceId = "MAV_ESC_HOT" } }
                }).HealthRules);
            var start = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

            var disarmed = evaluator.Evaluate(false, 3, 0, start);
            Assert.AreEqual(1f, disarmed.DataOk);
            Assert.AreEqual(1f, disarmed.EscOk);
            Assert.AreEqual(1f, disarmed.GpsRedundancyOk);

            evaluator.Evaluate(true, 3, 0, start);
            evaluator.RecordNamedValue("MAV_ESC_HOT", float.NaN, start);
            var grace = evaluator.Evaluate(true, 3, 0, start.AddSeconds(4));
            Assert.AreEqual(1f, grace.EscOk);

            var afterGrace = evaluator.Evaluate(true, 3, 0, start.AddSeconds(5));
            Assert.AreEqual(1f, afterGrace.DataOk, "Any named-value arrival keeps display data healthy.");
            Assert.AreEqual(0f, afterGrace.EscOk, "ESC health requires a finite ESC value.");
            Assert.AreEqual(0f, afterGrace.GpsRedundancyOk, "Exactly one GPS fix is a redundancy failure.");

            evaluator.RecordNamedValue("MAV_ESC_HOT", 0, start.AddSeconds(5));
            var restored = evaluator.Evaluate(true, 3, 3, start.AddSeconds(6));
            Assert.AreEqual(1f, restored.EscOk);
            Assert.AreEqual(1f, restored.GpsRedundancyOk);
        }

        [TestMethod]
        public void Validate_RejectsUnboundedEvaluationRate()
        {
            var rules = Judicar2600BundleProfile.Create(new BsaQuickViewProfile
            {
                Rows = 1, Columns = 1, Cells = { new BsaQuickViewCell { Position = 1 } }
            }).HealthRules;
            rules.EvaluationHz = 5;
            Assert.ThrowsException<System.IO.InvalidDataException>(() => JudicarHealthEvaluator.Validate(rules));
        }
    }
}
