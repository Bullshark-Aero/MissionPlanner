using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Classifies a Settings.config key name against a KeyPolicyConfig. Rules are tried in file order,
    /// first match wins; a key matching no rule falls to policy.Default. Match strings are a
    /// pipe-separated set of glob patterns ("*"/"?"), matched case-insensitively against the whole key
    /// name (anchored, not a substring search) - "speech*" matches "speechenable" but not
    /// "myspeechenable".
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
                    if (MatchesAny(key, rule.Match))
                        return rule.Class ?? policy.Default ?? KeyClass.MachineSpecific;
                }
            }

            return policy.Default ?? KeyClass.MachineSpecific;
        }

        static bool MatchesAny(string key, string pipeSeparatedGlobs)
        {
            if (string.IsNullOrWhiteSpace(pipeSeparatedGlobs))
                return false;

            foreach (var glob in pipeSeparatedGlobs.Split('|'))
            {
                var trimmed = glob.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (Regex.IsMatch(key, GlobToRegexPattern(trimmed), RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        static string GlobToRegexPattern(string glob)
        {
            var sb = new StringBuilder("^");
            foreach (var c in glob)
            {
                switch (c)
                {
                    case '*':
                        sb.Append(".*");
                        break;
                    case '?':
                        sb.Append('.');
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            sb.Append('$');
            return sb.ToString();
        }
    }
}
