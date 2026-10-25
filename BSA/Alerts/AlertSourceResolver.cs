using System;
using System.Collections.Generic;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public interface IAlertTelemetry
    {
        int SysId { get; }
        bool TryReadField(string property, out double value);
        DateTime? PacketUtc(uint messageId);
        NamedValueSample Named(string name);
        string NamedValueSlot(string name);
    }

    public sealed class AlertReading
    {
        public bool HasValue { get; set; }
        public double Value { get; set; }
        public bool Fresh { get; set; }
        public long SampleKey { get; set; }
        public string DisplayField { get; set; }

        public bool Usable => HasValue && Fresh && !double.IsNaN(Value) && !double.IsInfinity(Value);
    }

    public static class AlertSourceResolver
    {
        public const double DefaultFreshSeconds = 2;

        static readonly Dictionary<string, MAVLink.MAVLINK_MSG_ID> FieldMessages =
            new Dictionary<string, MAVLink.MAVLINK_MSG_ID>(StringComparer.OrdinalIgnoreCase)
            {
                { "rpm1", MAVLink.MAVLINK_MSG_ID.RPM },
                { "rpm2", MAVLink.MAVLINK_MSG_ID.RPM },
                { "airspeed", MAVLink.MAVLINK_MSG_ID.VFR_HUD },
                { "groundspeed", MAVLink.MAVLINK_MSG_ID.VFR_HUD },
                { "climbrate", MAVLink.MAVLINK_MSG_ID.VFR_HUD },
                { "gpsstatus", MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT },
                { "satcount", MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT },
                { "gpshdop", MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT },
                { "gpsstatus2", MAVLink.MAVLINK_MSG_ID.GPS2_RAW },
                { "satcount2", MAVLink.MAVLINK_MSG_ID.GPS2_RAW },
                { "gpshdop2", MAVLink.MAVLINK_MSG_ID.GPS2_RAW },
                { "battery_voltage", MAVLink.MAVLINK_MSG_ID.SYS_STATUS },
                { "battery_remaining", MAVLink.MAVLINK_MSG_ID.SYS_STATUS },
                { "current", MAVLink.MAVLINK_MSG_ID.SYS_STATUS }
            };

        public static uint MessageFor(string field) =>
            (uint)(field != null && FieldMessages.TryGetValue(field, out var id) ? id : MAVLink.MAVLINK_MSG_ID.HEARTBEAT);

        public static double FreshSecondsOf(AlertOptions alert) =>
            alert == null || alert.FreshSeconds <= 0 ? DefaultFreshSeconds : alert.FreshSeconds;

        public static AlertReading Read(AlertOptions alert, IAlertTelemetry telemetry, DateTime nowUtc)
        {
            if (alert == null || telemetry == null) return new AlertReading();
            var source = AlertSource.Parse(alert.Source);
            var window = FreshSecondsOf(alert);
            switch (source.Kind)
            {
                case AlertSourceKind.Field: return ReadField(source.Name, telemetry, nowUtc, window);
                case AlertSourceKind.Named: return ReadNamed(source.Name, telemetry, nowUtc, window);
                case AlertSourceKind.GpsFixCount: return ReadGpsFixCount(telemetry, nowUtc, window);
                case AlertSourceKind.NamedFreshCount: return ReadNamedFreshCount(alert.SourceNames, telemetry, nowUtc, window);
                default: return new AlertReading();
            }
        }

        public static bool IsFresh(DateTime receivedUtc, DateTime nowUtc, double windowSeconds)
        {
            var age = (nowUtc - receivedUtc).TotalSeconds;
            return age >= 0 && age <= windowSeconds;
        }

        static AlertReading ReadField(string property, IAlertTelemetry telemetry, DateTime nowUtc, double window)
        {
            if (!telemetry.TryReadField(property, out var value)) return new AlertReading { DisplayField = property };
            var received = telemetry.PacketUtc(MessageFor(property));
            return new AlertReading
            {
                HasValue = true,
                Value = value,
                Fresh = received.HasValue && IsFresh(received.Value, nowUtc, window),
                SampleKey = received?.Ticks ?? 0,
                DisplayField = property
            };
        }

        static AlertReading ReadNamed(string name, IAlertTelemetry telemetry, DateTime nowUtc, double window)
        {
            var sample = telemetry.Named(name);
            var slot = telemetry.NamedValueSlot(name);
            if (sample == null) return new AlertReading { DisplayField = slot };
            return new AlertReading
            {
                HasValue = true,
                Value = sample.LastFiniteValue,
                Fresh = sample.LastFiniteUtc != DateTime.MinValue && IsFresh(sample.LastFiniteUtc, nowUtc, window),
                SampleKey = sample.LastFiniteUtc.Ticks,
                DisplayField = slot
            };
        }

        static AlertReading ReadGpsFixCount(IAlertTelemetry telemetry, DateTime nowUtc, double window)
        {
            var count = 0;
            var latest = DateTime.MinValue;
            foreach (var receiver in new[]
            {
                new { Field = "gpsstatus", Message = MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT },
                new { Field = "gpsstatus2", Message = MAVLink.MAVLINK_MSG_ID.GPS2_RAW }
            })
            {
                var received = telemetry.PacketUtc((uint)receiver.Message);
                if (!received.HasValue || !IsFresh(received.Value, nowUtc, window)) continue;
                if (received.Value > latest) latest = received.Value;
                if (telemetry.TryReadField(receiver.Field, out var fix) && fix >= 3) count++;
            }
            return new AlertReading { HasValue = true, Value = count, Fresh = true, SampleKey = nowUtc.Ticks };
        }

        static AlertReading ReadNamedFreshCount(IEnumerable<string> names, IAlertTelemetry telemetry, DateTime nowUtc, double window)
        {
            var count = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names ?? new List<string>())
            {
                var normalized = AlertSource.NormalizeName(name);
                if (normalized.Length == 0 || !seen.Add(normalized)) continue;
                var sample = telemetry.Named(normalized);
                if (sample != null && IsFresh(sample.LastSeenUtc, nowUtc, window)) count++;
            }
            return new AlertReading { HasValue = true, Value = count, Fresh = true, SampleKey = nowUtc.Ticks };
        }
    }
}
