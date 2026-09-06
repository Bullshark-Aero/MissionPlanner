using BSA.Judicar2600.MissionPlannerPlugins;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class Judicar2600LightsStateTests
    {
        private const int OnPwm = 1900;
        private const int OffPwm = 1000;

        [TestMethod]
        public void UnknownState_FirstCommandEstablishesDefaultOn()
        {
            Assert.AreEqual(
                OnPwm,
                Judicar2600LightsState.NextTargetPwm(
                    LightsCommandState.Unknown,
                    OnPwm,
                    OffPwm));
        }

        [TestMethod]
        public void AcceptedCommands_AlternateBetweenOffAndOn()
        {
            Assert.AreEqual(
                OffPwm,
                Judicar2600LightsState.NextTargetPwm(
                    LightsCommandState.CommandedOn,
                    OnPwm,
                    OffPwm));
            Assert.AreEqual(
                OnPwm,
                Judicar2600LightsState.NextTargetPwm(
                    LightsCommandState.CommandedOff,
                    OnPwm,
                    OffPwm));
        }

        [TestMethod]
        public void FullyAcceptedPair_RecordsRequestedCommandState()
        {
            Assert.AreEqual(
                LightsCommandState.CommandedOff,
                Judicar2600LightsState.ResolveAfterAttempt(
                    OffPwm, OnPwm, true, true, false, false));
            Assert.AreEqual(
                LightsCommandState.CommandedOn,
                Judicar2600LightsState.ResolveAfterAttempt(
                    OnPwm, OnPwm, true, true, false, false));
        }

        [TestMethod]
        public void PartialPair_FullyAcceptedRecoveryRecordsOn()
        {
            Assert.AreEqual(
                LightsCommandState.CommandedOn,
                Judicar2600LightsState.ResolveAfterAttempt(
                    OffPwm, OnPwm, true, false, true, true));
        }

        [TestMethod]
        public void PartialPair_IncompleteRecoveryLeavesStateUnknown()
        {
            Assert.AreEqual(
                LightsCommandState.Unknown,
                Judicar2600LightsState.ResolveAfterAttempt(
                    OffPwm, OnPwm, false, true, true, false));
        }

        [TestMethod]
        public void LinkLossReconnectAndVehicleChangeInvalidateState()
        {
            Assert.IsTrue(Judicar2600LightsState.ConnectionInvalidatesState(true, 1, false, 0));
            Assert.IsTrue(Judicar2600LightsState.ConnectionInvalidatesState(false, 0, true, 1));
            Assert.IsTrue(Judicar2600LightsState.ConnectionInvalidatesState(true, 1, true, 2));
            Assert.IsFalse(Judicar2600LightsState.ConnectionInvalidatesState(true, 1, true, 1));
        }
    }
}
