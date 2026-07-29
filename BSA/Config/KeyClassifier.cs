using System;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Classifies a Settings.config key name against a KeyPolicyConfig. Rules are tried in file order,
    /// first match wins; a key matching no rule falls to policy.Default. Match strings are a
    /// pipe-separated set of glob patterns ("*"/"?"), matched case-insensitively against the whole key
    /// name (anchored, not a substring search) - "speech*" matches "speechenable" but not
    /// "myspeechenable". Glob logic lives in BSA.Core.GlobMatcher, shared with WP3's LockActionMatcher.
    /// </summary>
    public static class KeyClassifier
    {
        public static KeyClass Classify(string key, KeyPolicyConfig policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var rule = FindMatchingRule(key, policy);
            return rule?.Class ?? policy.Default ?? KeyClass.MachineSpecific;
        }

        /// <summary>The specific rule that classifies this key, or null if none matched (the key
        /// falls to policy.Default). Used by WP2 Phase B's diff grouping - keys classified by the same
        /// rule are grouped together in the import preview so a coupled pair (e.g. guided_alt /
        /// guided_alt_frame, both matched by "guided_alt*") can never be applied independently.</summary>
        public static KeyPolicyRule FindMatchingRule(string key, KeyPolicyConfig policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            if (string.IsNullOrEmpty(key))
                return null;

            foreach (var rule in policy.Rules ?? new System.Collections.Generic.List<KeyPolicyRule>())
            {
                if (GlobMatcher.MatchesAny(key, rule.Match))
                    return rule;
            }

            return null;
        }
    }
}
