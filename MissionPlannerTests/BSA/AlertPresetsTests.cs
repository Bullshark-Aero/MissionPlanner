using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertPresetsTests
    {
        static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void EveryPreset_IsAValidAlertWithItsOwnId()
        {
            var presets = AlertPresets.All();

            Assert.AreEqual(5, presets.Count);
            CollectionAssert.AreEqual(AlertPresets.Ids, presets.Select(p => p.Alert.Id).ToArray());
            Assert.AreEqual(0, AircraftAlertsForm.Validate(presets).Count,
                string.Join("; ", AircraftAlertsForm.Validate(presets)));
            Assert.IsTrue(presets.All(p => string.IsNullOrEmpty(p.Name)));
        }

        [TestMethod]
        public void EveryPreset_RoundTripsThroughXml()
        {
            foreach (var preset in AlertPresets.All())
            {
                var copy = AircraftAlertsForm.Clone(preset);

                Assert.AreEqual(preset.Text, copy.Text);
                Assert.AreEqual(preset.RepeatTime, copy.RepeatTime);
                Assert.AreEqual(preset.ConditionType, copy.ConditionType);
                Assert.AreEqual(preset.Warning, copy.Warning);
                Assert.AreEqual(preset.Alert.Source, copy.Alert.Source);
                Assert.AreEqual(preset.Alert.Phase, copy.Alert.Phase);
                Assert.AreEqual(preset.Alert.Enabled, copy.Alert.Enabled);
                Assert.AreEqual(preset.Alert.Preflight, copy.Alert.Preflight);
                CollectionAssert.AreEqual(preset.Alert.SourceNames, copy.Alert.SourceNames);
            }
        }

        [TestMethod]
        public void EngineOut_MatchesWhatWasAgreed()
        {
            var engine = AlertPresets.EngineOut();

            Assert.AreEqual("field:rpm1", engine.Alert.Source);
            Assert.AreEqual(CustomWarning.Conditional.LT, engine.ConditionType);
            Assert.AreEqual(1000, engine.Warning);
            Assert.AreEqual(1, engine.Alert.HoldSeconds);
            Assert.AreEqual(AlertPhase.Airborne, engine.Alert.Phase);
            Assert.AreEqual(AlertPhaseUnknown.AlertAnyway, engine.Alert.WhenPhaseUnknown);
            Assert.AreEqual("Engine failure", engine.Text);
            Assert.AreEqual(10, engine.RepeatTime);
            Assert.AreEqual("Engine RPM lost", engine.Alert.StaleText);
            Assert.AreEqual(AlertPreflight.None, engine.Alert.Preflight);
        }

        [TestMethod]
        public void AirspeedReadout_IsOffByDefaultAndOnlyInForwardFlight()
        {
            var airspeed = AlertPresets.AirspeedReadout();

            Assert.IsFalse(airspeed.Alert.Enabled);
            Assert.AreEqual(AlertKind.Readout, airspeed.Alert.Kind);
            Assert.IsTrue(airspeed.Alert.Always);
            Assert.AreEqual(AlertPhase.Forward, airspeed.Alert.Phase);
            Assert.AreEqual(AlertPhaseUnknown.StayQuiet, airspeed.Alert.WhenPhaseUnknown);
            Assert.AreEqual(3, AlertEvaluator.RepeatSeconds(airspeed));
        }

        [TestMethod]
        public void HealthPresets_CarryTheAgreedPreflightSeverities()
        {
            Assert.AreEqual(AlertPreflight.Critical, AlertPresets.Create(AlertPresets.AircraftDataLostId).Alert.Preflight);
            Assert.AreEqual(AlertPreflight.Warning, AlertPresets.Create(AlertPresets.EscTelemetryLostId).Alert.Preflight);
            Assert.AreEqual(AlertPreflight.Critical, AlertPresets.Create(AlertPresets.GpsRedundancyLostId).Alert.Preflight);
            var dataLost = AlertPresets.Create(AlertPresets.AircraftDataLostId);
            Assert.AreEqual(Judicar2600BundleProfile.NamedFields.Length, dataLost.Alert.SourceNames.Count);
            Assert.IsTrue(dataLost.Alert.SourceNames.All(n => !n.StartsWith("MAV_")));
        }

        [TestMethod]
        public void UnknownPreset_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => AlertPresets.Create("nope"));
        }

        [TestMethod]
        public void UniqueId_AddsASuffixWhenTaken()
        {
            var existing = new List<CustomWarning> { AlertPresets.EngineOut() };

            Assert.AreEqual("engine-out-2", AlertPresets.UniqueId("engine-out", existing));
            Assert.AreEqual("airspeed-readout", AlertPresets.UniqueId("airspeed-readout", existing));
            Assert.AreEqual("alert", AlertPresets.UniqueId(" ", null));
        }

        static AlertContext Flying(double seconds, byte landed, byte vtol) => new AlertContext
        {
            NowUtc = T0.AddSeconds(seconds),
            Armed = true,
            PhaseKnown = true,
            LandedState = landed,
            VtolState = vtol
        };

        static Func<AlertOptions, AlertReading> Rpm(double rpm, long sample) =>
            alert => new AlertReading { HasValue = true, Value = rpm, Fresh = true, SampleKey = sample, DisplayField = "rpm1" };

        [TestMethod]
        public void EngineOut_FiresInHoverAndForwardFlightButNotOnTheGround()
        {
            foreach (var vtol in new byte[] { 3, 4 })
            {
                var evaluator = new AlertEvaluator();
                var list = new[] { AlertPresets.EngineOut() };
                evaluator.Evaluate(list, Flying(0, 2, vtol), Rpm(400, 1));
                var decision = evaluator.Evaluate(list, Flying(1.2, 2, vtol), Rpm(400, 2)).Decisions.Single();
                Assert.AreEqual("Engine failure", decision.SpeakText, "vtol state " + vtol);
            }

            var ground = new AlertEvaluator();
            var onGround = new[] { AlertPresets.EngineOut() };
            ground.Evaluate(onGround, Flying(0, 1, 3), Rpm(0, 1));
            Assert.IsNull(ground.Evaluate(onGround, Flying(2, 1, 3), Rpm(0, 2)).Decisions.Single().SpeakText);
        }

        [TestMethod]
        public void AirspeedReadout_SpeaksOnlyInTransitionAndForwardFlight()
        {
            var airspeed = AlertPresets.AirspeedReadout();
            airspeed.Alert.Enabled = true;
            var list = new[] { airspeed };
            Func<AlertOptions, AlertReading> speed = alert => new AlertReading { HasValue = true, Value = 22.4, Fresh = true, SampleKey = 1 };

            Assert.AreEqual("22", new AlertEvaluator().Evaluate(list, Flying(0, 2, 4), speed).Decisions.Single().SpeakText);
            Assert.AreEqual("22", new AlertEvaluator().Evaluate(list, Flying(0, 2, 2), speed).Decisions.Single().SpeakText);
            Assert.IsNull(new AlertEvaluator().Evaluate(list, Flying(0, 2, 3), speed).Decisions.Single().SpeakText);
            Assert.IsNull(new AlertEvaluator().Evaluate(list, new AlertContext { NowUtc = T0, Armed = true }, speed)
                .Decisions.Single().SpeakText);
        }

        [TestMethod]
        public void AircraftDataLost_WaitsForTheArmedGraceThenFires()
        {
            var evaluator = new AlertEvaluator();
            var list = new[] { AlertPresets.Create(AlertPresets.AircraftDataLostId) };
            Func<AlertOptions, AlertReading> none = alert => new AlertReading { HasValue = true, Value = 0, Fresh = true, SampleKey = 1 };
            var armed = new AlertContext { NowUtc = T0, Armed = true };

            Assert.IsNull(evaluator.Evaluate(list, armed, none).Decisions.Single().SpeakText);
            armed.NowUtc = T0.AddSeconds(5);
            Assert.AreEqual("Judicar data lost", evaluator.Evaluate(list, armed, none).Decisions.Single().SpeakText);
        }
    }
}
