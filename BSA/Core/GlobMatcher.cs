using System.Text;
using System.Text.RegularExpressions;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Anchored, case-insensitive glob matching shared by every BSA policy that classifies a name
    /// against a pipe-separated set of patterns ("*"/"?") - originally written for
    /// BSA.Config.KeyClassifier, reused as-is by BSA.Lock.LockActionMatcher (same algorithm, different
    /// data: Settings.config keys vs. MAVLink param names). Matches the whole name, not a substring -
    /// "speech*" matches "speechenable" but not "myspeechenable".
    /// </summary>
    public static class GlobMatcher
    {
        public static bool MatchesAny(string value, string pipeSeparatedGlobs)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(pipeSeparatedGlobs))
                return false;

            foreach (var glob in pipeSeparatedGlobs.Split('|'))
            {
                var trimmed = glob.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (Regex.IsMatch(value, ToRegexPattern(trimmed), RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        static string ToRegexPattern(string glob)
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
