using System;
using System.Text.RegularExpressions;

namespace MissionPlanner.BSA.Config
{
    /// <summary>SemVer precedence, with legacy four-part numeric MP versions retained.
    /// Build metadata does not affect compatibility. No upstream-to-BSMP version mapping.</summary>
    public sealed class BsmpVersion : IComparable<BsmpVersion>
    {
        static readonly Regex Pattern = new Regex(
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\.(0|[1-9][0-9]*))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
            RegexOptions.CultureInvariant);
        readonly string[] numbers;
        readonly string[] prerelease;
        readonly string text;

        BsmpVersion(string text, string[] numbers, string[] prerelease)
        {
            this.text = text;
            this.numbers = numbers;
            this.prerelease = prerelease;
        }

        public static bool TryParse(string value, out BsmpVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(value) || value.Length > 256) return false;
            var match = Pattern.Match(value);
            if (!match.Success) return false;
            // Four-part versions are legacy numeric identities, not SemVer.
            if (match.Groups[4].Success && (match.Groups[5].Success || match.Groups[6].Success))
                return false;
            var pre = match.Groups[5].Success ? match.Groups[5].Value.Split('.') : new string[0];
            foreach (var id in pre)
                if (IsNumeric(id) && id.Length > 1 && id[0] == '0') return false;
            version = new BsmpVersion(value, new[] { match.Groups[1].Value,
                match.Groups[2].Value, match.Groups[3].Value,
                match.Groups[4].Success ? match.Groups[4].Value : "0" }, pre);
            return true;
        }

        static bool IsNumeric(string value)
        {
            foreach (var c in value) if (c < '0' || c > '9') return false;
            return true;
        }

        static int CompareNumeric(string left, string right) =>
            left.Length != right.Length ? left.Length.CompareTo(right.Length) :
            string.CompareOrdinal(left, right);

        public int CompareTo(BsmpVersion other)
        {
            if (other == null) return 1;
            for (var i = 0; i < numbers.Length; i++)
            {
                var result = CompareNumeric(numbers[i], other.numbers[i]);
                if (result != 0) return result;
            }
            if (prerelease.Length == 0 || other.prerelease.Length == 0)
                return (prerelease.Length == 0 ? 1 : 0).CompareTo(other.prerelease.Length == 0 ? 1 : 0);
            for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
            {
                var left = prerelease[i];
                var right = other.prerelease[i];
                var ln = IsNumeric(left);
                var rn = IsNumeric(right);
                var result = ln && rn ? CompareNumeric(left, right) :
                    ln != rn ? (ln ? -1 : 1) : string.CompareOrdinal(left, right);
                if (result != 0) return result;
            }
            return prerelease.Length.CompareTo(other.prerelease.Length);
        }

        public override string ToString() => text;
    }
}
