using System.Collections.Generic;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Shared value-preview formatting for the diff-showing UIs (ImportDiffPanel, ComparePackageForm) -
    /// single source of truth so the two views can't drift on truncation length/behavior.
    /// </summary>
    public static class ConfigValueDisplay
    {
        /// <summary>Values can be huge (e.g. displayview is an embedded JSON blob) - truncate for
        /// single-line display; the full value is still what's compared/applied, this is display only.</summary>
        public static string Preview(string value)
        {
            if (value == null)
                return "";

            return value.Length > 24 ? value.Substring(0, 21) + "..." : value;
        }

        public static string Preview(IReadOnlyDictionary<string, string> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out var value))
                return "";

            return Preview(value);
        }
    }
}
