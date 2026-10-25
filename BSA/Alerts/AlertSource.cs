using System;

namespace MissionPlanner.BSA.Alerts
{
    public enum AlertSourceKind
    {
        Unknown,
        Field,
        Named,
        GpsFixCount,
        NamedFreshCount
    }

    public sealed class AlertSource
    {
        public const string FieldPrefix = "field:";
        public const string NamedPrefix = "named:";
        public const string GpsFixCount = "computed:gps-fix-count";
        public const string NamedFreshCount = "computed:named-fresh-count";

        public AlertSourceKind Kind { get; private set; }
        public string Name { get; private set; }

        public static AlertSource Parse(string source)
        {
            var text = (source ?? "").Trim();
            if (text.StartsWith(FieldPrefix, StringComparison.OrdinalIgnoreCase) && text.Length > FieldPrefix.Length)
                return new AlertSource { Kind = AlertSourceKind.Field, Name = text.Substring(FieldPrefix.Length).Trim() };
            if (text.StartsWith(NamedPrefix, StringComparison.OrdinalIgnoreCase) && text.Length > NamedPrefix.Length)
            {
                var name = NormalizeName(text.Substring(NamedPrefix.Length));
                if (name.Length > 0) return new AlertSource { Kind = AlertSourceKind.Named, Name = name };
            }
            if (string.Equals(text, GpsFixCount, StringComparison.OrdinalIgnoreCase))
                return new AlertSource { Kind = AlertSourceKind.GpsFixCount, Name = "" };
            if (string.Equals(text, NamedFreshCount, StringComparison.OrdinalIgnoreCase))
                return new AlertSource { Kind = AlertSourceKind.NamedFreshCount, Name = "" };
            return new AlertSource { Kind = AlertSourceKind.Unknown, Name = text };
        }

        public static string Field(string property) => FieldPrefix + property;

        public static string Named(string name) => NamedPrefix + NormalizeName(name);

        public static string NormalizeName(string name)
        {
            var text = (name ?? "").Trim();
            if (text.StartsWith("MAV_", StringComparison.OrdinalIgnoreCase)) text = text.Substring(4);
            return text.ToUpperInvariant();
        }
    }
}
