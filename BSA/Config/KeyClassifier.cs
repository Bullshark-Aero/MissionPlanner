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

            if (!string.IsNullOrEmpty(key))
            {
                foreach (var rule in policy.Rules ?? new System.Collections.Generic.List<KeyPolicyRule>())
                {
                    if (GlobMatcher.MatchesAny(key, rule.Match))
                        return rule.Class ?? policy.Default ?? KeyClass.MachineSpecific;
                }
            }

            return policy.Default ?? KeyClass.MachineSpecific;
        }
    }
}
