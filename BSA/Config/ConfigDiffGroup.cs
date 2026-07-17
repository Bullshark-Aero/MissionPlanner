using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// One group of a ConfigCompareResult's keys, all classified by the same key-policy rule.
    /// MismatchedKeys and PackageOnlyKeys have a package value to apply; LiveOnlyKeys are
    /// informational only (the package has no value for them - nothing to apply, nothing changes).
    /// </summary>
    public class ConfigDiffGroup
    {
        public string GroupKey { get; set; }
        public List<string> MismatchedKeys { get; } = new List<string>();
        public List<string> LiveOnlyKeys { get; } = new List<string>();
        public List<string> PackageOnlyKeys { get; } = new List<string>();

        /// <summary>Keys in this group that have a package value available to apply (Mismatched +
        /// PackageOnly). This is the set an "apply this group" checkbox actually controls.</summary>
        public IEnumerable<string> ApplicableKeys => MismatchedKeys.Concat(PackageOnlyKeys);

        public bool HasAnyChange => MismatchedKeys.Count > 0 || LiveOnlyKeys.Count > 0 || PackageOnlyKeys.Count > 0;
    }

    /// <summary>
    /// Groups a compare result by matching key-policy rule rather than per-key, so the import preview
    /// can never let an operator apply half of a coupled pair (see ConfigDiffGroup's doc comment).
    /// Coarser than a hand-curated pairing model - e.g. distunits/speedunits/altunits get bundled
    /// together despite being logically independent - but this is a deliberate, documented trade-off:
    /// it can over-group unrelated keys, but it can never split a genuinely coupled pair, since any
    /// two keys sharing a rule always move together.
    /// </summary>
    public static class ConfigDiffGrouping
    {
        public const string UngroupedKey = "<default>";

        public static List<ConfigDiffGroup> Group(ConfigCompareResult result, KeyPolicyConfig policy)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var groups = new Dictionary<string, ConfigDiffGroup>(StringComparer.Ordinal);

            void AddTo(string key, Action<ConfigDiffGroup> add)
            {
                var rule = KeyClassifier.FindMatchingRule(key, policy);
                var groupKey = rule?.Match ?? UngroupedKey;
                if (!groups.TryGetValue(groupKey, out var group))
                {
                    group = new ConfigDiffGroup { GroupKey = groupKey };
                    groups[groupKey] = group;
                }
                add(group);
            }

            foreach (var key in result.MismatchedKeys) AddTo(key, g => g.MismatchedKeys.Add(key));
            foreach (var key in result.LiveOnlyKeys) AddTo(key, g => g.LiveOnlyKeys.Add(key));
            foreach (var key in result.PackageOnlyKeys) AddTo(key, g => g.PackageOnlyKeys.Add(key));

            return groups.Values.OrderBy(g => g.GroupKey, StringComparer.Ordinal).ToList();
        }
    }
}
