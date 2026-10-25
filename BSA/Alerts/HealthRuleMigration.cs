using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using MissionPlanner.BSA.Config;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public static class HealthRuleMigration
    {
        public static readonly string[] LegacyFields = { "J26_DATA_OK", "J26_ESC_OK", "J26_GPS_RED_OK" };

        public static bool IsLegacySpokenWarning(CustomWarning warning)
        {
            if (warning == null || warning.Alert != null || warning.type != CustomWarning.WarningType.SpeakAndText)
                return false;
            for (var item = warning; item != null; item = item.Child)
            {
                if (LegacyFields.Contains(item.Name, StringComparer.Ordinal)) return true;
            }
            return false;
        }

        public static List<CustomWarning> Migrate(IEnumerable<CustomWarning> warnings, BsaHealthRuleSet rules)
        {
            var result = (warnings ?? Enumerable.Empty<CustomWarning>()).Where(w => w != null && !IsLegacySpokenWarning(w)).ToList();
            foreach (var rule in rules?.Rules ?? new List<BsaHealthRule>())
            {
                var alert = PresetFor(rule);
                if (alert == null) continue;
                if (result.Any(w => string.Equals(w.Alert?.Id, alert.Alert.Id, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(alert);
            }
            return result;
        }

        public static CustomWarning PresetFor(BsaHealthRule rule)
        {
            if (rule == null) return null;
            var fresh = rule.FreshnessSeconds > 0 ? rule.FreshnessSeconds : 5;
            var grace = Math.Max(0, rule.ArmedGraceSeconds);
            switch (rule.OutputFieldId)
            {
                case "J26_DATA_OK":
                    var names = rule.InputFieldIds != null && rule.InputFieldIds.Count > 0
                        ? (IEnumerable<string>)rule.InputFieldIds
                        : Judicar2600BundleProfile.NamedFields;
                    return AlertPresets.AircraftDataLost(names, fresh, grace);
                case "J26_ESC_OK":
                    var esc = AlertPresets.EscTelemetryLost(fresh, grace);
                    var input = rule.InputFieldIds?.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                    if (input != null) esc.Alert.Source = AlertSource.Named(input);
                    return esc;
                case "J26_GPS_RED_OK":
                    return AlertPresets.GpsRedundancyLost(grace);
                default:
                    return null;
            }
        }

        public static List<CustomWarning> Parse(string warningsXml)
        {
            var serializer = new XmlSerializer(typeof(List<CustomWarning>), new[] { typeof(CustomWarning) });
            using (var reader = new StringReader(warningsXml))
                return (List<CustomWarning>)serializer.Deserialize(reader) ?? new List<CustomWarning>();
        }

        public static string Serialize(IEnumerable<CustomWarning> warnings)
        {
            var serializer = new XmlSerializer(typeof(List<CustomWarning>), new[] { typeof(CustomWarning) });
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    serializer.Serialize(writer, (warnings ?? Enumerable.Empty<CustomWarning>()).ToList());
                return new UTF8Encoding(false).GetString(stream.ToArray());
            }
        }

        public static string WarningsToInstall(ConfigPackageContents package, bool installWarnings, string currentWarningsXml)
        {
            if (package == null || !installWarnings) return null;
            if (package.HealthRules == null) return package.WarningsXml;
            List<CustomWarning> source;
            if (package.WarningsXml != null)
            {
                source = Parse(package.WarningsXml);
            }
            else
            {
                try
                {
                    source = currentWarningsXml == null ? new List<CustomWarning>() : Parse(currentWarningsXml);
                }
                catch
                {
                    source = new List<CustomWarning>();
                }
            }
            return Serialize(Migrate(source, package.HealthRules));
        }

        public static bool WouldChange(string warningsToInstall, string currentWarningsXml)
        {
            if (warningsToInstall == null) return false;
            if (currentWarningsXml == null) return true;
            try
            {
                return Serialize(Parse(currentWarningsXml)) != Serialize(Parse(warningsToInstall));
            }
            catch
            {
                return true;
            }
        }

        public static string Summary(string warningsToInstall, bool fromBundle)
        {
            if (warningsToInstall == null) return string.Empty;
            List<CustomWarning> warnings;
            try
            {
                warnings = Parse(warningsToInstall);
            }
            catch
            {
                return string.Empty;
            }
            var alerts = warnings.Count(w => w.Alert != null);
            var classic = warnings.Count - alerts;
            return fromBundle
                ? "\n\nAlerts and warnings: replaced with the bundle's " + Count(alerts, "alert") + " and " + Count(classic, "warning") + "."
                : "\n\nAlerts and warnings: this machine's are kept, with the bundle's health checks as alerts (" +
                  Count(alerts, "alert") + " and " + Count(classic, "warning") + " in all).";
        }

        static string Count(int count, string noun) => count + " " + noun + (count == 1 ? "" : "s");
    }
}
