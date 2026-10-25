using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Config;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public static class AlertPresets
    {
        public const string EngineOutId = "engine-out";
        public const string AircraftDataLostId = "aircraft-data-lost";
        public const string EscTelemetryLostId = "esc-telemetry-lost";
        public const string GpsRedundancyLostId = "gps-redundancy-lost";
        public const string AirspeedReadoutId = "airspeed-readout";

        public static readonly string[] Ids =
        {
            EngineOutId, AircraftDataLostId, EscTelemetryLostId, GpsRedundancyLostId, AirspeedReadoutId
        };

        public static readonly string[] HealthIds = { AircraftDataLostId, EscTelemetryLostId, GpsRedundancyLostId };

        public static List<CustomWarning> All() => Ids.Select(Create).ToList();

        public static CustomWarning Create(string id)
        {
            switch (id)
            {
                case EngineOutId: return EngineOut();
                case AircraftDataLostId: return AircraftDataLost(Judicar2600BundleProfile.NamedFields, 5, 5);
                case EscTelemetryLostId: return EscTelemetryLost(5, 5);
                case GpsRedundancyLostId: return GpsRedundancyLost(5);
                case AirspeedReadoutId: return AirspeedReadout();
                default: throw new ArgumentException("Unknown alert preset: " + id, nameof(id));
            }
        }

        public static CustomWarning EngineOut() => new CustomWarning
        {
            Text = "Engine failure",
            RepeatTime = 10,
            ConditionType = CustomWarning.Conditional.LT,
            Warning = 1000,
            color = "Red",
            Alert = new AlertOptions
            {
                Id = EngineOutId,
                Title = "Engine out",
                Kind = AlertKind.Alarm,
                Source = AlertSource.Field("rpm1"),
                HoldSeconds = 1,
                FreshSeconds = 2,
                Phase = AlertPhase.Airborne,
                WhenPhaseUnknown = AlertPhaseUnknown.AlertAnyway,
                StaleText = "Engine RPM lost",
                Show = true,
                Preflight = AlertPreflight.None
            }
        };

        public static CustomWarning AircraftDataLost(IEnumerable<string> namedFields, double freshSeconds, double graceSeconds) => new CustomWarning
        {
            Text = "Judicar data lost",
            RepeatTime = 10,
            ConditionType = CustomWarning.Conditional.EQ,
            Warning = 0,
            Alert = new AlertOptions
            {
                Id = AircraftDataLostId,
                Title = "Aircraft data lost",
                Kind = AlertKind.Alarm,
                Source = AlertSource.NamedFreshCount,
                SourceNames = namedFields.Select(AlertSource.NormalizeName).Distinct().ToList(),
                FreshSeconds = freshSeconds,
                ArmedGraceSeconds = graceSeconds,
                Phase = AlertPhase.Armed,
                WhenPhaseUnknown = AlertPhaseUnknown.AlertAnyway,
                Preflight = AlertPreflight.Critical
            }
        };

        public static CustomWarning EscTelemetryLost(double freshSeconds, double graceSeconds) => new CustomWarning
        {
            Text = "ESC telemetry lost",
            RepeatTime = 10,
            ConditionType = CustomWarning.Conditional.NONE,
            Alert = new AlertOptions
            {
                Id = EscTelemetryLostId,
                Title = "ESC telemetry lost",
                Kind = AlertKind.Alarm,
                Source = AlertSource.Named("ESC_HOT"),
                TriggerOnStale = true,
                FreshSeconds = freshSeconds,
                ArmedGraceSeconds = graceSeconds,
                Phase = AlertPhase.Armed,
                WhenPhaseUnknown = AlertPhaseUnknown.AlertAnyway,
                Preflight = AlertPreflight.Warning
            }
        };

        public static CustomWarning GpsRedundancyLost(double graceSeconds) => new CustomWarning
        {
            Text = "GPS redundancy lost",
            RepeatTime = 10,
            ConditionType = CustomWarning.Conditional.EQ,
            Warning = 1,
            Alert = new AlertOptions
            {
                Id = GpsRedundancyLostId,
                Title = "GPS redundancy lost",
                Kind = AlertKind.Alarm,
                Source = AlertSource.GpsFixCount,
                FreshSeconds = 3,
                ArmedGraceSeconds = graceSeconds,
                Phase = AlertPhase.Armed,
                WhenPhaseUnknown = AlertPhaseUnknown.AlertAnyway,
                Preflight = AlertPreflight.Critical
            }
        };

        public static CustomWarning AirspeedReadout() => new CustomWarning
        {
            Text = "{value}",
            RepeatTime = 3,
            ConditionType = CustomWarning.Conditional.NONE,
            Alert = new AlertOptions
            {
                Id = AirspeedReadoutId,
                Title = "Airspeed readout",
                Enabled = false,
                Kind = AlertKind.Readout,
                Source = AlertSource.Field("airspeed"),
                Always = true,
                FreshSeconds = 2,
                Phase = AlertPhase.Forward,
                WhenPhaseUnknown = AlertPhaseUnknown.StayQuiet,
                Preflight = AlertPreflight.None
            }
        };

        public static string UniqueId(string wanted, IEnumerable<CustomWarning> existing)
        {
            var taken = new HashSet<string>(
                (existing ?? Enumerable.Empty<CustomWarning>()).Where(w => w?.Alert?.Id != null).Select(w => w.Alert.Id),
                StringComparer.OrdinalIgnoreCase);
            var stem = string.IsNullOrWhiteSpace(wanted) ? "alert" : wanted.Trim();
            if (!taken.Contains(stem)) return stem;
            for (var i = 2; ; i++)
            {
                var candidate = stem + "-" + i;
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
