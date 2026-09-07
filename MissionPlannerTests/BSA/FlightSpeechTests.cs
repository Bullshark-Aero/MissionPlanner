using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Speech;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class FlightSpeechTests
    {
        [TestMethod]
        public void StateGatesCoverBothTransitionsAndExcludeGroundUnknownDisarmed()
        {
            foreach (byte phase in new byte[] { 1, 2, 4 }) Assert.IsTrue(FlightSpeech.IsForwardOrTransition(phase));
            foreach (byte phase in new byte[] { 0, 3, 255 }) Assert.IsFalse(FlightSpeech.IsForwardOrTransition(phase));
            foreach (byte phase in new byte[] { 2, 3, 4 })
            {
                Assert.IsTrue(FlightSpeech.IsAirborne(128, phase));
                Assert.IsFalse(FlightSpeech.IsAirborne(0, phase));
            }
            foreach (byte phase in new byte[] { 0, 1, 255 }) Assert.IsFalse(FlightSpeech.IsAirborne(128, phase));
        }

        [TestMethod]
        public void FreshnessRejectsOldUnsetAndFutureTimestamps()
        {
            var now = DateTime.UtcNow;
            Assert.IsTrue(FlightSpeech.IsFresh(now.AddSeconds(-1), now, 1));
            Assert.IsFalse(FlightSpeech.IsFresh(now.AddSeconds(-1.01), now, 1));
            Assert.IsFalse(FlightSpeech.IsFresh(DateTime.MinValue, now, 3));
            Assert.IsFalse(FlightSpeech.IsFresh(now.AddSeconds(1), now, 3));
        }

        [TestMethod]
        public void OneIsolatedRpmSampleCannotAgeIntoAnAlert()
        {
            var s = new FlightSpeech();
            Assert.IsNull(s.Update(0, true, false, 20, 0, false, true, true, 123));
            Assert.IsNull(s.Update(1.1, true, false, 20, 0, false, true, true, 123));
            Assert.AreEqual("Engine failure", s.Update(1.2, true, false, 20, 0, false, true, true, 124));
        }

        [TestMethod]
        public void SpeedIsNumericAndEveryThreeSecondsWithoutBand()
        {
            var s = new FlightSpeech();
            Assert.AreEqual("20", s.Update(0, true, true, 19.5, 2000, true, true, true));
            Assert.IsNull(s.Update(2.99, true, true, 30, 2000, true, true, true));
            Assert.AreEqual("30", s.Update(3, true, true, 30, 2000, true, true, true));
            Assert.AreEqual("0", s.Update(6, true, true, 0, 2000, true, true, true));
        }

        [TestMethod]
        public void NoSpeedInHoverOrOnGround()
        {
            var s = new FlightSpeech();
            Assert.IsNull(s.Update(0, true, false, 20, 2000, true, true, true));
            Assert.IsNull(s.Update(3, false, true, 20, 2000, true, true, true));
        }

        [TestMethod]
        public void EngineRequiresMoreThanOneSecondAndRepeatsEveryTen()
        {
            var s = new FlightSpeech();
            Assert.IsNull(s.Update(0, true, false, 20, 999, true, true, true));
            Assert.IsNull(s.Update(1, true, false, 20, 999, true, true, true));
            Assert.AreEqual("Engine failure", s.Update(1.1, true, false, 20, 999, true, true, true));
            Assert.IsNull(s.Update(11, true, false, 20, 999, true, true, true));
            Assert.AreEqual("Engine failure", s.Update(11.2, true, false, 20, 999, true, true, true));
        }

        [TestMethod]
        public void RecoveryOrMissingDataRestartsHold()
        {
            var s = new FlightSpeech();
            s.Update(0, true, false, 20, 0, false, true, true);
            s.Update(0.8, true, false, 20, 1000, false, true, true);
            Assert.IsNull(s.Update(1.2, true, false, 20, 0, false, true, true));
            s.Update(2, true, false, 20, null, false, true, true);
            Assert.IsNull(s.Update(3, true, false, 20, 0, false, true, true));
            Assert.AreEqual("Engine failure", s.Update(4.1, true, false, 20, 0, false, true, true));
        }

        [TestMethod]
        public void EngineTakesPriorityAndBusySpeechDoesNotQueueOldSpeed()
        {
            var s = new FlightSpeech();
            Assert.IsNull(s.Update(0, true, true, 20, 0, true, true, false));
            Assert.IsNull(s.Update(3, true, true, 21, 0, true, true, false));
            Assert.AreEqual("Engine failure", s.Update(4, true, true, 22, 0, true, true, true));
            Assert.IsNull(s.Update(5, true, true, 23, 0, true, true, true));
            Assert.AreEqual("24", s.Update(7, true, true, 24, 0, true, true, true));
        }

        [TestMethod]
        public void InvalidValuesAndDisabledFeaturesAreSilent()
        {
            var s = new FlightSpeech();
            Assert.IsNull(s.Update(0, true, true, double.NaN, double.NaN, true, true, true));
            Assert.IsNull(s.Update(3, true, true, double.PositiveInfinity, -1, true, true, true));
            Assert.IsNull(s.Update(6, true, true, 20, 0, false, false, true));
        }

        [TestMethod]
        public void ResetClearsPendingFailure()
        {
            var s = new FlightSpeech();
            s.Update(0, true, false, 20, 0, true, true, true);
            s.Reset();
            Assert.IsNull(s.Update(2, true, false, 20, 0, true, true, true));
        }
    }
}
