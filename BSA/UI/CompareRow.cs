using System;
using System.Collections.Generic;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    /// <summary>One setting's status in a compare/diff report, flattened out of a ConfigDiffGroup for
    /// display in a grid - grouping only matters for the Import wizard's "apply as a coupled unit"
    /// checkbox, not for a read-only compare report, so this view intentionally discards it.</summary>
    public enum CompareRowStatus
    {
        Changed,
        OnlyInPackage,
        OnlyOnThisMachine
    }

    public class CompareRow
    {
        public CompareRowStatus Status { get; set; }
        public string Key { get; set; }
        public string CurrentValue { get; set; }
        public string PackageValue { get; set; }

        /// <summary>Flattens diff groups into one row per key, sorted by key name (Ordinal, matching
        /// ConfigCompareEngine.Normalize's sort) - a flat alphabetical view is the right default for a
        /// read-only report, unlike the Import wizard's per-group checkbox rows.</summary>
        public static List<CompareRow> FromGroups(List<ConfigDiffGroup> groups,
            IReadOnlyDictionary<string, string> liveValues, IReadOnlyDictionary<string, string> packageValues)
        {
            var rows = new List<CompareRow>();
            if (groups == null)
                return rows;

            foreach (var group in groups)
            {
                foreach (var key in group.MismatchedKeys)
                {
                    rows.Add(new CompareRow
                    {
                        Status = CompareRowStatus.Changed,
                        Key = key,
                        CurrentValue = ValueOrEmpty(liveValues, key),
                        PackageValue = ValueOrEmpty(packageValues, key)
                    });
                }

                foreach (var key in group.PackageOnlyKeys)
                {
                    rows.Add(new CompareRow
                    {
                        Status = CompareRowStatus.OnlyInPackage,
                        Key = key,
                        CurrentValue = "",
                        PackageValue = ValueOrEmpty(packageValues, key)
                    });
                }

                foreach (var key in group.LiveOnlyKeys)
                {
                    rows.Add(new CompareRow
                    {
                        Status = CompareRowStatus.OnlyOnThisMachine,
                        Key = key,
                        CurrentValue = ValueOrEmpty(liveValues, key),
                        PackageValue = ""
                    });
                }
            }

            rows.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
            return rows;
        }

        static string ValueOrEmpty(IReadOnlyDictionary<string, string> values, string key) =>
            values != null && values.TryGetValue(key, out var value) && value != null ? value : "";
    }
}
