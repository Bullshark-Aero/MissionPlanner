using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertEvaluatorTests
    {
        static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        static CustomWarning EngineOut(double hold = 1, int repeat = 10) => new CustomWarning
        {
            Text = "Engine failure",
            RepeatTime = repeat,
            ConditionType = CustomWarning.Conditional.LT,
            Warning = 1000,
            Alert = new AlertOptions
            {
                Id = "engine-out",
                Title = "Engine out",
                Kind = AlertKind.Alarm,
                Source = "field:rpm1",
                HoldSeconds = hold,
                Phase = AlertPhase.Any,
                StaleText = "Engine RPM lost"
            }
        };

        static CustomWarning Readout(int repeat = 3) => new CustomWarning
        {
            Text = "{value}",
            RepeatTime = repeat,
            Alert = new AlertOptions
            {
                Id = "airspeed",
                Title = "Airspeed",
                Kind = AlertKind.Readout,
                Source = "field:airspeed",
                Always = true,
                Phase = AlertPhase.Any
            }
        };

        static AlertContext At(double seconds, bool armed = true) =>
            new AlertContext { NowUtc = T0.AddSeconds(seconds), Armed = armed };

        static Func<AlertOptions, AlertReading> Value(double value, long sample, bool fresh = true) =>
            alert => new AlertReading { HasValue = true, Value = value, Fresh = fresh, SampleKey = sample, DisplayField = "rpm1" };

        static AlertDecision Only(AlertTickResult result) => result.Decisions.Single();

        [TestMethod]
        public void Hold_FiresOnlyAfterHoldTimeWithANewerSample()
        {
            var evaluator = new AlertEvaluator();
            var alert = EngineOut();
            var list = new[] { alert };

            Assert.IsNull(Only(evaluator.Evaluate(list, At(0), Value(500, 1))).SpeakText);
            Assert.IsNull(Only(evaluator.Evaluate(list, At(0.5), Value(500, 2))).SpeakText);
            var fired = Only(evaluator.Evaluate(list, At(1.1), Value(500, 3)));

            Assert.AreEqual(AlertMode.Triggered, fired.Mode);
            Assert.AreEqual("Engine failure", fired.SpeakText);
            Assert.IsTrue(fired.IsAlarm);
        }

        [TestMethod]
        public void Hold_OneGlitchSampleNeverAgesIntoAnAlarm()
        {
            var evaluator = new AlertEvaluator();
            var list = new[] { EngineOut() };

            evaluator.Evaluate(list, At(0), Value(500, 1));
            var later = Only(evaluator.Evaluate(list, At(1.5), Value(500, 1)));

            Assert.AreEqual(AlertMode.Quiet, later.Mode);
            Assert.IsNull(later.SpeakText);
        }

        [TestMethod]
        public void Hold_RestartsWhenTheConditionClears()
        {
            var evaluator = new AlertEvaluator();
            var list = new[] { EngineOut() };

            evaluator.Evaluate(list, At(0), Value(500, 1));
            evaluator.Evaluate(list, At(0.6), Value(2000, 2));
            evaluator.Evaluate(list, At(0.8), Value(500, 3));
            Assert.IsNull(Only(evaluator.Evaluate(list, At(1.2), Value(500, 4))).SpeakText);
            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(1.9), Value(500, 5))).SpeakText);
        }

        [TestMethod]
        public void Repeat_WaitsForTheRepeatTimeAfterItWasSpoken()
        {
            var evaluator = new AlertEvaluator();
            var alert = EngineOut(hold: 0);
            var list = new[] { alert };

            var first = Only(evaluator.Evaluate(list, At(0), Value(500, 1)));
            Assert.IsNotNull(first.SpeakText);
            evaluator.MarkSpoken(alert, T0);

            Assert.IsNull(Only(evaluator.Evaluate(list, At(5), Value(500, 2))).SpeakText);
            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(10), Value(500, 3))).SpeakText);
        }

        [TestMethod]
        public void Repeat_AnUnspokenAlarmStaysDue()
        {
            var evaluator = new AlertEvaluator();
            var list = new[] { EngineOut(hold: 0) };

            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(0), Value(500, 1))).SpeakText);
            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(0.1), Value(500, 2))).SpeakText);
        }

        [TestMethod]
        public void Repeat_FlappingConditionIsRateLimited()
        {
            var evaluator = new AlertEvaluator();
            var alert = EngineOut(hold: 0);
            var list = new[] { alert };

            evaluator.Evaluate(list, At(0), Value(500, 1));
            evaluator.MarkSpoken(alert, T0);
            evaluator.Evaluate(list, At(0.2), Value(2000, 2));

            Assert.IsNull(Only(evaluator.Evaluate(list, At(0.4), Value(500, 3))).SpeakText);
            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(1.0), Value(500, 4))).SpeakText);
        }

        [TestMethod]
        public void Repeat_ReadoutsNeverRepeatFasterThanThreeSeconds()
        {
            Assert.AreEqual(3, AlertEvaluator.RepeatSeconds(Readout(repeat: 1)));
            Assert.AreEqual(1, AlertEvaluator.RepeatSeconds(EngineOut(repeat: 0)));
            Assert.AreEqual(10, AlertEvaluator.RepeatSeconds(EngineOut(repeat: 10)));
        }

        [TestMethod]
        public void Readout_SpeaksTheRoundedValue()
        {
            var evaluator = new AlertEvaluator();
            var decision = Only(evaluator.Evaluate(new[] { Readout() }, At(0), Value(17.5, 1)));

            Assert.AreEqual("18", decision.SpeakText);
            Assert.IsFalse(decision.IsAlarm);
        }

        [TestMethod]
        public void Readout_IsSilentWhenTheValueIsStale()
        {
            var evaluator = new AlertEvaluator();
            var decision = Only(evaluator.Evaluate(new[] { Readout() }, At(0), Value(17, 1, fresh: false)));

            Assert.AreEqual(AlertMode.Stale, decision.Mode);
            Assert.IsNull(decision.SpeakText);
        }

        [TestMethod]
        public void Stale_SpeaksTheStaleTextInsteadOfTheAlarm()
        {
            var evaluator = new AlertEvaluator();
            var decision = Only(evaluator.Evaluate(new[] { EngineOut() }, At(0), Value(0, 1, fresh: false)));

            Assert.AreEqual(AlertMode.Stale, decision.Mode);
            Assert.AreEqual("Engine RPM lost", decision.SpeakText);
            Assert.IsTrue(decision.IsAlarm);
        }

        [TestMethod]
        public void Stale_NonFiniteValuesCountAsStale()
        {
            var evaluator = new AlertEvaluator();
            var decision = Only(evaluator.Evaluate(new[] { EngineOut() }, At(0), Value(double.NaN, 1)));

            Assert.AreEqual(AlertMode.Stale, decision.Mode);
        }

        [TestMethod]
        public void TriggerOnStale_AlarmsWhenTheDataStops()
        {
            var alert = new CustomWarning
            {
                Text = "ESC telemetry lost",
                Alert = new AlertOptions { Id = "esc", Kind = AlertKind.Alarm, Source = "named:ESC_HOT", TriggerOnStale = true }
            };
            var evaluator = new AlertEvaluator();

            var stale = Only(evaluator.Evaluate(new[] { alert }, At(0), Value(1, 1, fresh: false)));
            Assert.AreEqual(AlertMode.Triggered, stale.Mode);
            Assert.AreEqual("ESC telemetry lost", stale.SpeakText);

            var fresh = Only(evaluator.Evaluate(new[] { alert }, At(1), Value(1, 2)));
            Assert.AreEqual(AlertMode.Quiet, fresh.Mode);
        }

        [TestMethod]
        public void ArmedGrace_HoldsAlarmsBackAfterArming()
        {
            var alert = EngineOut(hold: 0);
            alert.Alert.ArmedGraceSeconds = 5;
            var evaluator = new AlertEvaluator();
            var list = new[] { alert };

            Assert.IsNull(Only(evaluator.Evaluate(list, At(0), Value(500, 1))).SpeakText);
            Assert.IsNull(Only(evaluator.Evaluate(list, At(4.9), Value(500, 2))).SpeakText);
            Assert.IsNotNull(Only(evaluator.Evaluate(list, At(5), Value(500, 3))).SpeakText);

            evaluator.Evaluate(list, At(6, armed: false), Value(500, 4));
            Assert.IsNull(Only(evaluator.Evaluate(list, At(7), Value(500, 5))).SpeakText);
        }

        [TestMethod]
        public void Disabled_AlertsStayQuiet()
        {
            var alert = EngineOut(hold: 0);
            alert.Alert.Enabled = false;

            var decision = Only(new AlertEvaluator().Evaluate(new[] { alert }, At(0), Value(500, 1)));

            Assert.AreEqual(AlertMode.Quiet, decision.Mode);
            Assert.IsNull(decision.SpeakText);
        }

        [TestMethod]
        public void PhaseGate_OutsideThePhaseTheAlertIsQuiet()
        {
            var alert = EngineOut(hold: 0);
            alert.Alert.Phase = AlertPhase.Airborne;
            var context = At(0);
            context.PhaseKnown = true;
            context.LandedState = 1;

            var decision = Only(new AlertEvaluator().Evaluate(new[] { alert }, context, Value(500, 1)));

            Assert.AreEqual(AlertMode.Quiet, decision.Mode);
        }

        [TestMethod]
        public void Colour_IsRaisedWhileTriggeredAndClearedOnce()
        {
            var alert = EngineOut(hold: 0);
            alert.Alert.Show = true;
            alert.color = "Red";
            var evaluator = new AlertEvaluator();
            var list = new[] { alert };

            var on = evaluator.Evaluate(list, At(0), Value(500, 1)).Colours.Single();
            Assert.AreEqual("rpm1", on.Field);
            Assert.AreEqual("Red", on.Colour);

            var off = evaluator.Evaluate(list, At(1), Value(2000, 2)).Colours.Single();
            Assert.AreEqual(AlertEvaluator.NoColour, off.Colour);

            Assert.AreEqual(0, evaluator.Evaluate(list, At(2), Value(2000, 3)).Colours.Count);
        }

        [TestMethod]
        public void ColourOnly_ColoursTheQuickViewAndNeverSpeaks()
        {
            var alert = EngineOut(hold: 0);
            alert.Text = "";
            alert.Alert.StaleText = "";
            alert.Alert.Show = true;
            alert.color = "Orange";
            var evaluator = new AlertEvaluator();
            var list = new[] { alert };

            var fired = evaluator.Evaluate(list, At(0), Value(500, 1));
            Assert.AreEqual(AlertMode.Triggered, Only(fired).Mode);
            Assert.IsNull(Only(fired).SpeakText);
            Assert.AreEqual("Orange", fired.Colours.Single().Colour);

            Assert.IsNull(Only(evaluator.Evaluate(list, At(30), Value(500, 2))).SpeakText);
            var stale = evaluator.Evaluate(list, At(31), Value(500, 3, fresh: false));
            Assert.AreEqual(AlertMode.Stale, Only(stale).Mode);
            Assert.IsNull(Only(stale).SpeakText);
        }

        [TestMethod]
        public void Colour_IsClearedWhenTheAlertIsRemoved()
        {
            var alert = EngineOut(hold: 0);
            alert.Alert.Show = true;
            alert.color = "Red";
            var evaluator = new AlertEvaluator();

            evaluator.Evaluate(new[] { alert }, At(0), Value(500, 1));
            var cleared = evaluator.Evaluate(new CustomWarning[0], At(1), Value(500, 2)).Colours.Single();

            Assert.AreEqual("rpm1", cleared.Field);
            Assert.AreEqual(AlertEvaluator.NoColour, cleared.Colour);
        }

        [TestMethod]
        public void ClassicWarningsAreIgnored()
        {
            var result = new AlertEvaluator().Evaluate(new[] { new CustomWarning() }, At(0), Value(1, 1));

            Assert.AreEqual(0, result.Decisions.Count);
        }

        static AlertDecision Due(bool alarm, string text) => new AlertDecision { IsAlarm = alarm, SpeakText = text };

        [TestMethod]
        public void Arbiter_AlarmCutsAReadoutShort()
        {
            var alarm = Due(true, "Engine failure");

            var chosen = AlertSpeechArbiter.Choose(new[] { alarm }, false, true, out var cancel);

            Assert.AreSame(alarm, chosen);
            Assert.IsTrue(cancel);
        }

        [TestMethod]
        public void Arbiter_AlarmNeverCutsOtherSpeech()
        {
            var chosen = AlertSpeechArbiter.Choose(new[] { Due(true, "Engine failure") }, false, false, out var cancel);

            Assert.IsNull(chosen);
            Assert.IsFalse(cancel);
        }

        [TestMethod]
        public void Arbiter_AlarmGoesBeforeAReadout()
        {
            var readout = Due(false, "18");
            var alarm = Due(true, "Engine failure");

            var chosen = AlertSpeechArbiter.Choose(new[] { readout, alarm }, true, false, out var cancel);

            Assert.AreSame(alarm, chosen);
            Assert.IsFalse(cancel);
        }

        [TestMethod]
        public void Arbiter_ReadoutWaitsForSilence()
        {
            Assert.IsNull(AlertSpeechArbiter.Choose(new[] { Due(false, "18") }, false, true, out _));
            Assert.IsNotNull(AlertSpeechArbiter.Choose(new[] { Due(false, "18") }, true, false, out _));
            Assert.IsNull(AlertSpeechArbiter.Choose(new List<AlertDecision> { new AlertDecision() }, true, false, out _));
        }
    }
}
