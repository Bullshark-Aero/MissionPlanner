using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class AlertPreflightCheck : IRegisteredCheck
    {
        public const string KeyPrefix = "alert.";
        public const string IdPrefix = "alert-";
        public const string PreferredGroup = "Telemetry & Sensors";
        public const string FallbackGroup = "Aircraft alerts";

        readonly CustomWarning _warning;
        readonly Func<IAlertTelemetry> _telemetry;
        readonly Func<DateTime> _clock;

        public AlertPreflightCheck(CustomWarning warning, Func<IAlertTelemetry> telemetry, Func<DateTime> clock)
        {
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
            if (warning.Alert == null) throw new ArgumentException("Only alerts can be pre-flight checks.", nameof(warning));
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public string Key => KeyPrefix + _warning.Alert.Id;

        public CheckOutcome Evaluate(PreflightCheckDefinition check, out string detail)
        {
            var alert = _warning.Alert;
            var watch = AlertDescriptions.WatchLabel(alert);
            var telemetry = _telemetry();
            if (telemetry == null)
            {
                detail = "No aircraft connected.";
                return CheckOutcome.Unknown;
            }

            var reading = AlertSourceResolver.Read(alert, telemetry, _clock());
            if (!reading.Usable)
            {
                detail = "No fresh data from " + watch + " in the last " +
                         AlertDescriptions.Seconds(AlertSourceResolver.FreshSecondsOf(alert)) + ".";
                return alert.TriggerOnStale ? CheckOutcome.Fail : CheckOutcome.Unknown;
            }

            var value = watch + " is " + AlertDescriptions.Number(reading.Value);
            if (alert.Always || alert.TriggerOnStale)
            {
                detail = value + ".";
                return CheckOutcome.Pass;
            }
            if (AlertEvaluator.Compare(_warning.ConditionType, reading.Value, _warning.Warning))
            {
                detail = value + ": the alert triggers when it is " + AlertDescriptions.TriggerLabel(_warning) + ".";
                return CheckOutcome.Fail;
            }
            detail = value + ".";
            return CheckOutcome.Pass;
        }

        public static List<CustomWarning> PreflightAlerts(IEnumerable<CustomWarning> warnings) =>
            (warnings ?? Enumerable.Empty<CustomWarning>())
            .Where(w => w?.Alert != null && w.Alert.Enabled && w.Alert.Preflight != AlertPreflight.None &&
                        !string.IsNullOrWhiteSpace(w.Alert.Id))
            .GroupBy(w => w.Alert.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        public static void Register(RegisteredCheckRegistry registry, IEnumerable<CustomWarning> alerts,
            Func<IAlertTelemetry> telemetry, Func<DateTime> clock)
        {
            foreach (var alert in PreflightAlerts(alerts))
                registry.Register(new AlertPreflightCheck(alert, telemetry, clock));
        }

        public static List<PreflightCheckDefinition> Append(IEnumerable<PreflightCheckDefinition> checks,
            PreflightChecklistMetadata metadata, IEnumerable<CustomWarning> alerts)
        {
            var result = (checks ?? Enumerable.Empty<PreflightCheckDefinition>()).ToList();
            var ids = new HashSet<string>(result.Select(c => c.Id).Where(id => id != null), StringComparer.OrdinalIgnoreCase);
            string group = null;
            if (metadata?.Groups != null && metadata.Groups.Count > 0)
            {
                group = metadata.Groups.FirstOrDefault(g => string.Equals(g, PreferredGroup, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    group = FallbackGroup;
                    if (!metadata.Groups.Contains(FallbackGroup, StringComparer.OrdinalIgnoreCase))
                        metadata.Groups.Add(FallbackGroup);
                }
            }

            foreach (var warning in PreflightAlerts(alerts))
            {
                var alert = warning.Alert;
                var id = IdPrefix + alert.Id;
                if (!ids.Add(id)) continue;
                result.Add(new PreflightCheckDefinition
                {
                    Id = id,
                    Title = "Alert: " + (string.IsNullOrWhiteSpace(alert.Title) ? alert.Id : alert.Title),
                    Type = CheckType.Auto,
                    Severity = alert.Preflight == AlertPreflight.Critical ? CheckSeverity.Critical : CheckSeverity.Warning,
                    Source = CheckSource.Telemetry,
                    Check = KeyPrefix + alert.Id,
                    AllowNotApplicable = false,
                    Group = group
                });
            }
            return result;
        }
    }
}
