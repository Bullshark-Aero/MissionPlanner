using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertFlightPhaseTests
    {
        static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        static AlertOptions Gate(AlertPhase phase, AlertKind kind = AlertKind.Alarm) => new AlertOptions
        {
            Phase = phase,
            Kind = kind,
            WhenPhaseUnknown = FlightPhase.DefaultUnknownRule(kind)
        };

        static AlertContext Known(byte landed, byte vtol, bool armed = true) =>
            new AlertContext { Armed = armed, PhaseKnown = true, LandedState = landed, VtolState = vtol };

        static AlertContext Unknown(bool armed = true) => new AlertContext { Armed = armed };

        [TestMethod]
        public void AnyAndArmed_IgnoreTheFlightPhase()
        {
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Any), Unknown(armed: false)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Armed), Unknown(armed: false)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Armed), Unknown()));
        }

        [TestMethod]
        public void Airborne_MeansInAirTakingOffOrLanding()
        {
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Airborne), Known(1, 3)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Airborne), Known(2, 3)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Airborne), Known(3, 3)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Airborne), Known(4, 4)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Airborne), Known(2, 4, armed: false)));
        }

        [TestMethod]
        public void Hover_IsAirborneMulticopterOnly()
        {
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Hover), Known(2, 3)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Hover), Known(2, 4)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Hover), Known(1, 3)));
        }

        [TestMethod]
        public void Forward_IncludesBothTransitions()
        {
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Forward), Known(2, 1)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Forward), Known(2, 2)));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Forward), Known(2, 4)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Forward), Known(2, 3)));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Forward), Known(1, 4)));
        }

        [TestMethod]
        public void UnknownPhaseWhileArmed_AlarmsActAndReadoutsStayQuiet()
        {
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Airborne), Unknown()));
            Assert.IsTrue(FlightPhase.Allows(Gate(AlertPhase.Hover), Unknown()));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Forward, AlertKind.Readout), Unknown()));
            Assert.IsFalse(FlightPhase.Allows(Gate(AlertPhase.Airborne), Unknown(armed: false)));
        }

        [TestMethod]
        public void NeedsPhase_OnlyForFlightPhases()
        {
            Assert.IsFalse(FlightPhase.NeedsPhase(AlertPhase.Any));
            Assert.IsFalse(FlightPhase.NeedsPhase(AlertPhase.Armed));
            Assert.IsTrue(FlightPhase.NeedsPhase(AlertPhase.Airborne));
            Assert.IsTrue(FlightPhase.NeedsPhase(AlertPhase.Hover));
            Assert.IsTrue(FlightPhase.NeedsPhase(AlertPhase.Forward));
        }

        [TestMethod]
        public void Requester_AsksAtOnceThenRetriesEveryFiveSeconds()
        {
            var sent = new List<int>();
            var requester = new FlightPhaseRequester(sent.Add);

            Assert.IsTrue(requester.Tick(T0, true, true, 1, false));
            Assert.IsFalse(requester.Tick(T0.AddSeconds(4.9), true, true, 1, false));
            Assert.IsTrue(requester.Tick(T0.AddSeconds(5), true, true, 1, false));
            CollectionAssert.AreEqual(new[] { 1, 1 }, sent);
        }

        [TestMethod]
        public void Requester_StaysQuietWhenNotNeededOrAlreadyReceiving()
        {
            var sent = new List<int>();
            var requester = new FlightPhaseRequester(sent.Add);

            Assert.IsFalse(requester.Tick(T0, false, true, 1, false));
            Assert.IsFalse(requester.Tick(T0, true, true, 1, true));
            Assert.IsFalse(requester.Tick(T0, true, false, 1, false));
            Assert.IsFalse(requester.Tick(T0, true, true, 0, false));
            Assert.AreEqual(0, sent.Count);
        }

        [TestMethod]
        public void Requester_AsksAgainAtOnceAfterReconnectOrNewVehicle()
        {
            var sent = new List<int>();
            var requester = new FlightPhaseRequester(sent.Add);

            requester.Tick(T0, true, true, 1, false);
            requester.Tick(T0.AddSeconds(1), true, false, 1, false);
            Assert.IsTrue(requester.Tick(T0.AddSeconds(2), true, true, 1, false));
            Assert.IsTrue(requester.Tick(T0.AddSeconds(3), true, true, 2, false));
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, sent);
        }

        [TestMethod]
        public void Requester_CommandAsksForExtendedSysStateAtTwoHertz()
        {
            var command = FlightPhaseRequester.Command(7);

            Assert.AreEqual((ushort)MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL, command.command);
            Assert.AreEqual((float)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE, command.param1);
            Assert.AreEqual(500000f, command.param2);
            Assert.AreEqual(7, command.target_system);
            Assert.AreEqual(1, command.target_component);
        }
    }
}
