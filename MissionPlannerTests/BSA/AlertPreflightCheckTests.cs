using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertPreflightCheckTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        sealed class FakeTelemetry : IAlertTelemetry
        {
            public readonly Dictionary<string, double> Fields = new Dictionary<string, double>();
            public readonly Dictionary<uint, DateTime> Packets = new Dictionary<uint, DateTime>();
            public readonly NamedValueTracker Tracker = new NamedValueTracker();
            public int SysId => 1;
            public bool TryReadField(string property, out double value) => Fields.TryGetValue(property, out value);
            public DateTime? PacketUtc(uint messageId) => Packets.TryGetValue(messageId, out var t) ? t : (DateTime?)null;
            public NamedValueSample Named(string name) => Tracker.Get(SysId, name);
            public string NamedValueSlot(string name) => null;
        }

        static CheckOutcome Run(CustomWarning alert, IAlertTelemetry telemetry, out string detail) =>
            new AlertPreflightCheck(alert, () => telemetry, () => Now).Evaluate(null, out detail);

        static FakeTelemetry Gps(double fix1, double fix2)
        {
            var telemetry = new FakeTelemetry();
            telemetry.Fields["gpsstatus"] = fix1;
            telemetry.Fields["gpsstatus2"] = fix2;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT] = Now;
            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.GPS2_RAW] = Now;
            return telemetry;
        }

        [TestMethod]
        public void NoAircraft_IsUnknown()
        {
            var outcome = Run(AlertPresets.EngineOut(), null, out var detail);

            Assert.AreEqual(CheckOutcome.Unknown, outcome);
            Assert.AreEqual("No aircraft connected.", detail);
        }

        [TestMethod]
        public void GpsRedundancy_FailsOnExactlyOneFix()
        {
            var gps = AlertPresets.Create(AlertPresets.GpsRedundancyLostId);

            Assert.AreEqual(CheckOutcome.Fail, Run(gps, Gps(3, 1), out var detail));
            StringAssert.Contains(detail, "GPS receivers with a fix is 1");
            Assert.AreEqual(CheckOutcome.Pass, Run(gps, Gps(4, 3), out _));
        }

        [TestMethod]
        public void EscTelemetry_FailsWhenStaleAndPassesWhenFresh()
        {
            var esc = AlertPresets.Create(AlertPresets.EscTelemetryLostId);
            var telemetry = new FakeTelemetry();

            Assert.AreEqual(CheckOutcome.Fail, Run(esc, telemetry, out var detail));
            StringAssert.Contains(detail, "No fresh data from ESC_HOT");

            telemetry.Tracker.Record(1, "ESC_HOT", 40, Now.AddSeconds(-1));
            Assert.AreEqual(CheckOutcome.Pass, Run(esc, telemetry, out _));
        }

        [TestMethod]
        public void AircraftDataLost_FailsWithNoFreshValues()
        {
            var dataLost = AlertPresets.Create(AlertPresets.AircraftDataLostId);
            var telemetry = new FakeTelemetry();

            Assert.AreEqual(CheckOutcome.Fail, Run(dataLost, telemetry, out _));

            telemetry.Tracker.Record(1, "LIDAR_M", 2, Now);
            Assert.AreEqual(CheckOutcome.Pass, Run(dataLost, telemetry, out _));
        }

        [TestMethod]
        public void StaleFieldWithoutStaleTrigger_IsUnknown()
        {
            var engine = AlertPresets.EngineOut();
            var telemetry = new FakeTelemetry();
            telemetry.Fields["rpm1"] = 0;

            Assert.AreEqual(CheckOutcome.Unknown, Run(engine, telemetry, out _));

            telemetry.Packets[(uint)MAVLink.MAVLINK_MSG_ID.RPM] = Now;
            Assert.AreEqual(CheckOutcome.Fail, Run(engine, telemetry, out var detail));
            StringAssert.Contains(detail, "below 1000");
        }

        [TestMethod]
        public void Register_AddsOneCheckPerEnabledPreflightAlert()
        {
            var registry = new RegisteredCheckRegistry();
            var disabled = AlertPresets.Create(AlertPresets.GpsRedundancyLostId);
            disabled.Alert.Id = "off";
            disabled.Alert.Enabled = false;

            AlertPreflightCheck.Register(registry, AlertPresets.All().Concat(new[] { disabled, new CustomWarning() }),
                () => null, () => Now);

            CollectionAssert.AreEquivalent(
                AlertPresets.HealthIds.Select(id => AlertPreflightCheck.KeyPrefix + id).ToArray(),
                registry.Keys.ToArray());
        }

        static PreflightChecklistMetadata Metadata(params string[] groups) => new PreflightChecklistMetadata
        {
            Name = "test",
            Groups = groups.Length == 0 ? null : groups.ToList()
        };

        [TestMethod]
        public void Append_AddsAutoChecksWithTheAlertsSeverity()
        {
            var existing = new List<PreflightCheckDefinition> { new PreflightCheckDefinition { Id = "first", Group = "Telemetry & Sensors" } };
            var metadata = Metadata("Mission", "Telemetry & Sensors");

            var checks = AlertPreflightCheck.Append(existing, metadata, AlertPresets.All());

            Assert.AreEqual(4, checks.Count);
            Assert.AreEqual(1, existing.Count, "the loaded checklist is not modified");
            var added = checks.Skip(1).ToList();
            CollectionAssert.AreEqual(AlertPresets.HealthIds.Select(id => "alert-" + id).ToArray(), added.Select(c => c.Id).ToArray());
            CollectionAssert.AreEqual(
                new CheckSeverity?[] { CheckSeverity.Critical, CheckSeverity.Warning, CheckSeverity.Critical },
                added.Select(c => c.Severity).ToArray());
            Assert.IsTrue(added.All(c => c.Type == CheckType.Auto && c.Group == "Telemetry & Sensors"));
            Assert.AreEqual("Alert: Aircraft data lost", added[0].Title);
            Assert.AreEqual("alert.aircraft-data-lost", added[0].Check);
            CollectionAssert.AreEqual(new[] { "Mission", "Telemetry & Sensors" }, metadata.Groups);
        }

        [TestMethod]
        public void Append_UsesItsOwnGroupWhenTheChecklistHasNoTelemetryGroup()
        {
            var metadata = Metadata("Mission");

            var checks = AlertPreflightCheck.Append(new PreflightCheckDefinition[0], metadata, AlertPresets.All());

            Assert.IsTrue(checks.All(c => c.Group == AlertPreflightCheck.FallbackGroup));
            CollectionAssert.AreEqual(new[] { "Mission", AlertPreflightCheck.FallbackGroup }, metadata.Groups);
        }

        [TestMethod]
        public void Append_LeavesGroupUnsetWithoutDeclaredGroups()
        {
            var checks = AlertPreflightCheck.Append(null, Metadata(), AlertPresets.All());

            Assert.IsTrue(checks.All(c => c.Group == null));
        }

        [TestMethod]
        public void Append_TheChecksRunThroughTheRunEngine()
        {
            var registry = new RegisteredCheckRegistry();
            var alerts = new[] { AlertPresets.Create(AlertPresets.GpsRedundancyLostId) };
            var telemetry = Gps(3, 0);
            AlertPreflightCheck.Register(registry, alerts, () => telemetry, () => Now);
            var checks = AlertPreflightCheck.Append(null, Metadata(), alerts);

            Assert.IsTrue(registry.TryGet(checks.Single().Check, out var check));
            Assert.AreEqual(CheckOutcome.Fail, check.Evaluate(checks.Single(), out _));
            Assert.AreEqual(0, PreflightChecklistLoader.Validate(new PreflightChecklistConfig
            {
                SchemaVersion = 1,
                Metadata = Metadata(),
                Checks = checks
            }, registry.Keys).Count());
        }
    }
}
