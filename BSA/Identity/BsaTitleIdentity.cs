using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MissionPlanner.BSA.Identity
{
    /// <summary>
    /// Builds the operator-visible application title from identities owned by BSMP and the connected autopilot.
    /// A BSA firmware version is displayed only when the autopilot explicitly publishes a valid semantic version.
    /// </summary>
    public static class BsaTitleIdentity
    {
        const string SemVer =
            @"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)" +
            @"(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?" +
            @"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?";

        static readonly Regex UpstreamFirmware = new Regex(
            @"^(?<product>[A-Za-z][A-Za-z0-9_-]*)\s+V(?<version>" + SemVer + @")(?=$|\s)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex BsaFirmware = new Regex(
            @"(?:^|\s)BSA\s+V(?<version>" + SemVer + @")(?=$|\s)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex SerialIdentity = new Regex(
            @"^(?<controller>.+?)\s+(?<word1>[0-9A-Fa-f]{8})\s+(?<word2>[0-9A-Fa-f]{8})\s+(?<word3>[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static readonly Regex HexUid = new Regex(
            @"^[0-9A-Fa-f]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string BuildBaseTitle(string productName, string productVersion)
        {
            return JoinParts(NormalizeWhitespace(productName), NormalizeWhitespace(productVersion));
        }

        public static string BuildConnectedTitle(
            string baseTitle,
            string firmwareBanner,
            string serialBanner,
            string canonicalUid2)
        {
            var title = NormalizeWhitespace(baseTitle);
            var firmware = NormalizeWhitespace(firmwareBanner);
            var serial = ParseSerialIdentity(serialBanner);
            var uid = NormalizeUid(canonicalUid2);

            if (string.IsNullOrEmpty(uid))
                uid = serial.FallbackUid;
            else if (!string.IsNullOrEmpty(serial.FallbackUid) &&
                     uid.StartsWith(serial.FallbackUid, StringComparison.OrdinalIgnoreCase) &&
                     Regex.IsMatch(uid.Substring(serial.FallbackUid.Length), @"^0*$"))
                uid = serial.FallbackUid;

            if (!string.IsNullOrEmpty(firmware))
            {
                var upstream = UpstreamFirmware.Match(firmware);
                var bsa = BsaFirmware.Match(firmware);

                if (upstream.Success && bsa.Success)
                {
                    firmware = upstream.Groups["product"].Value + " V" + bsa.Groups["version"].Value +
                               " Based on V" + upstream.Groups["version"].Value;
                }

                title = JoinParts(title, firmware);
            }

            title = JoinParts(title, serial.Controller, uid);
            return title;
        }

        static SerialParts ParseSerialIdentity(string serialBanner)
        {
            var serial = NormalizeWhitespace(serialBanner);
            if (string.IsNullOrEmpty(serial))
                return new SerialParts();

            var match = SerialIdentity.Match(serial);
            if (!match.Success)
                return new SerialParts {Controller = serial};

            return new SerialParts
            {
                Controller = match.Groups["controller"].Value,
                FallbackUid = ReverseWordBytes(match.Groups["word1"].Value) +
                              ReverseWordBytes(match.Groups["word2"].Value) +
                              ReverseWordBytes(match.Groups["word3"].Value)
            };
        }

        static string NormalizeUid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var compact = Regex.Replace(value, @"[\s:\-]", string.Empty);
            if (compact.Length < 16 || compact.Length > 64 || compact.Length % 2 != 0 ||
                !HexUid.IsMatch(compact) || Regex.IsMatch(compact, @"^0+$"))
                return string.Empty;

            return compact.ToUpperInvariant();
        }

        static string ReverseWordBytes(string word)
        {
            var reversed = new StringBuilder(word.Length);
            for (var offset = word.Length - 2; offset >= 0; offset -= 2)
                reversed.Append(word, offset, 2);
            return reversed.ToString().ToUpperInvariant();
        }

        static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        static string JoinParts(params string[] values)
        {
            var result = new StringBuilder();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (result.Length > 0)
                    result.Append(' ');
                result.Append(value);
            }

            return result.ToString();
        }

        sealed class SerialParts
        {
            public string Controller { get; set; }
            public string FallbackUid { get; set; }
        }
    }
}
