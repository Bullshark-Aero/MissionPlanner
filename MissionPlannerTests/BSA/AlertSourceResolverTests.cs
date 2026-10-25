using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertSourceResolverTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        sealed class FakeTelemetry : IAlertTelemetry
        {
            public readonly Dictionary<string, double> Fields = new Dictionary<string, double>();
            public readonly Dictionary<uint, DateTime> Packets = new Dictionary<uint, DateTime>();
            public readonly NamedValueTracker Tracker = new NamedValueTracker();
            public readonly Dictionary<string, string> Slots = new Dictionary<string, string>();

            public int SysId => 1;

            public bool TryReadField(string property, out double value) => Fields.TryGetValue(property, out value);

            public DateTime? PacketUtc(uint messageId) =>
                Packets.TryGetValue(messageId, out var time) ? time : (DateTime?)null;

            public NamedValueSample Named(string name) => Tracker.Get(SysId, name);

            public string NamedValueSlot(string name) =>
                Slots.TryGetValue("MAV_" + AlertSource.NormalizeName(name), out var slot) ? slot : null;
        }

        static AlertOptions Watch(string source, double fresh = 2, params string[] names) =>
            new AlertOptions { Source = source, FreshSeconds = fresh, SourceNames = new List<string>(names) };

        [TestMethod]
        public void Parse_RecognisesEverySourceKind()
        {
            Assert.AreEqual(AlertSourceKind.Field, AlertSource.Parse("field:rpm1").Kind);
            Assert.AreEqual("rpm1", AlertSource.Parse("field:rpm1").Name);
            Assert.AreEqual(AlertSourceKind.Named, AlertSource.Parse("named:mav_esc_hot").Kind);
            Assert.AreEqual("ESC_HOT", AlertSource.Parse("named:mav_esc_hot").Name);
            Assert.AreEqual(AlertSourceKind.GpsFixCount, AlertSource.Parse(AlertSource.GpsFixCount).Kind);
            Assert.AreEqual(AlertSourceKind.NamedFreshCount, AlertSource.Parse(AlertSource.NamedFreshCount).Kind);
            Assert.AreEqual(AlertSourceKind.Unknown, AlertSource.Parse("rpm1").Kind);
            Assert.AreEqual(AlertSourceKind.Unknown, AlertSource.Parse(null).Kind);
            Assert.AreEqual("named:ESC_HOT", AlertSource.Named("MAV_esc_hot"));
        }

        [TestMethod]
        public void Named_IsFoundByNameWhicheverSlotItLandedIn()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Tracker.Record(1, "ESC_HOT", 42, Now.AddSeconds(-1));
            telemetry.Slots["MAV_ESC_HOT"] = "customfield7";

            var reading = AlertSourceResolver.Read(Watch("named:ESC_HOT"), telemetry, Now);

            Assert.IsTrue(reading.Usable);
            Assert.AreEqual(42, reading.Value);
            Assert.AreEqual("customfield7", reading.DisplayField);
        }

        [TestMethod]
        public void Named_GoesStaleAfterTheFreshWindow()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Tracker.Record(1, "ESC_HOT", 42, Now.AddSeconds(-6));

            var reading = AlertSourceResolver.Read(Watch("named:ESC_HOT", fresh: 5), telemetry, Now);

            Assert.IsTrue(reading.HasValue);
            Assert.IsFalse(reading.Fresh);
            Assert.IsFalse(reading.Usable);
        }

        [TestMethod]
        public void Named_NonFiniteValuesGoStaleOnceTheLastFiniteOneIsOld()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Tracker.Record(1, "ESC_HOT", 7, Now.AddSeconds(-4));
            telemetry.Tracker.Record(1, "ESC_HOT", float.NaN, Now);
            var watch = Watch("named:ESC_HOT", fresh: 5);

            var reading = AlertSourceResolver.Read(watch, telemetry, Now);
            Assert.IsTrue(reading.Usable);
            Assert.AreEqual(7, reading.Value);

            Assert.IsFalse(AlertSourceResolver.Read(watch, telemetry, Now.AddSeconds(1.5)).Usable);
        }

        [TestMethod]
        public void Named_NeverReceivedHasNoValue()
        {
            var reading = AlertSourceResolver.Read(Watch("named:ESC_HOT"), new FakeTelemetry(), Now);

            Assert.IsFalse(reading.HasValue);
            Assert.IsFalse(reading.Usable);
        }

        [TestMethod]
        public void Field_FreshnessFollowsTheMessageThatCarriesIt()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Fields["rpm1"] = 2100;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.RPM] = Now.AddSeconds(-3);
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT] = Now;

            var reading = AlertSourceResolver.Read(Watch("field:rpm1"), telemetry, Now);

            Assert.IsTrue(reading.HasValue);
            Assert.IsFalse(reading.Fresh, "a live heartbeat must not make a dead RPM stream look fresh");
            Assert.AreEqual("rpm1", reading.DisplayField);
        }

        [TestMethod]
        public void Field_UnmappedFieldsUseTheHeartbeat()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Fields["alt"] = 120;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT] = Now.AddSeconds(-0.5);

            Assert.IsTrue(AlertSourceResolver.Read(Watch("field:alt"), telemetry, Now).Usable);
            Assert.AreEqual((uint)MAVLink.MAVLINK_MSG_ID.VFR_HUD, AlertSourceResolver.MessageFor("airspeed"));
        }

        [TestMethod]
        public void Field_UnknownPropertyHasNoValue()
        {
            Assert.IsFalse(AlertSourceResolver.Read(Watch("field:nonsense"), new FakeTelemetry(), Now).HasValue);
        }

        [TestMethod]
        public void GpsFixCount_CountsFreshReceiversWithA3DFix()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Fields["gpsstatus"] = 3;
            telemetry.Fields["gpsstatus2"] = 6;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT] = Now;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.GPS2_RAW] = Now;
            var watch = Watch(AlertSource.GpsFixCount);

            Assert.AreEqual(2, AlertSourceResolver.Read(watch, telemetry, Now).Value);

            telemetry.Fields["gpsstatus2"] = 2;
            Assert.AreEqual(1, AlertSourceResolver.Read(watch, telemetry, Now).Value);

            telemetry.Fields["gpsstatus2"] = 3;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.GPS2_RAW] = Now.AddSeconds(-10);
            var reading = AlertSourceResolver.Read(watch, telemetry, Now);
            Assert.AreEqual(1, reading.Value);
            Assert.IsTrue(reading.Usable);
        }

        [TestMethod]
        public void NamedFreshCount_CountsEachFreshNameOnce()
        {
            var telemetry = new FakeTelemetry();
            telemetry.Tracker.Record(1, "ESC_HOT", 1, Now.AddSeconds(-1));
            telemetry.Tracker.Record(1, "FUEL", 1, Now.AddSeconds(-9));
            telemetry.Tracker.Record(2, "BATT", 1, Now);

            var reading = AlertSourceResolver.Read(
                Watch(AlertSource.NamedFreshCount, 5, "MAV_ESC_HOT", "ESC_HOT", "FUEL", "BATT", "MISSING"), telemetry, Now);

            Assert.AreEqual(1, reading.Value);
            Assert.IsTrue(reading.Usable);
        }

        [TestMethod]
        public void Tracker_KeepsTheLastFiniteTimeSeparately()
        {
            var tracker = new NamedValueTracker();
            tracker.Record(1, "MAV_esc_hot", 3, Now.AddSeconds(-2));
            tracker.Record(1, "ESC_HOT", float.NaN, Now);

            var sample = tracker.Get(1, "esc_hot");

            Assert.IsTrue(float.IsNaN(sample.Value));
            Assert.AreEqual(Now, sample.LastSeenUtc);
            Assert.AreEqual(Now.AddSeconds(-2), sample.LastFiniteUtc);
            Assert.AreEqual(3, sample.LastFiniteValue);
            Assert.IsNull(tracker.Get(2, "ESC_HOT"));
            Assert.IsNull(tracker.Get(0, "ESC_HOT"));
        }
    }
}
