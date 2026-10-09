using System.Globalization;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public static class AlertDescriptions
    {
        public static string WatchLabel(AlertOptions alert)
        {
            var source = AlertSource.Parse(alert?.Source);
            switch (source.Kind)
            {
                case AlertSourceKind.Field: return source.Name;
                case AlertSourceKind.Named: return source.Name + " (named value)";
                case AlertSourceKind.GpsFixCount: return "GPS receivers with a fix";
                case AlertSourceKind.NamedFreshCount:
                    return "Fresh named values (" + (alert.SourceNames?.Count ?? 0) + ")";
                default: return "(nothing)";
            }
        }

        public static string ConditionText(CustomWarning.Conditional condition)
        {
            switch (condition)
            {
                case CustomWarning.Conditional.LT: return "below";
                case CustomWarning.Conditional.LTEQ: return "at or below";
                case CustomWarning.Conditional.EQ: return "equal to";
                case CustomWarning.Conditional.NEQ: return "not equal to";
                case CustomWarning.Conditional.GTEQ: return "at or above";
                case CustomWarning.Conditional.GT: return "above";
                default: return null;
            }
        }

        public static string TriggerLabel(CustomWarning warning)
        {
            var alert = warning.Alert;
            if (alert.TriggerOnStale) return "data stops for " + Seconds(AlertSourceResolver.FreshSecondsOf(alert));
            var condition = ConditionText(warning.ConditionType);
            var text = alert.Always
                ? "always"
                : condition == null
                    ? "no trigger"
                    : condition + " " + Number(warning.Warning);
            if (alert.HoldSeconds > 0) text += " for " + Seconds(alert.HoldSeconds);
            return text;
        }

        public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Seconds(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + " s";
    }
}
