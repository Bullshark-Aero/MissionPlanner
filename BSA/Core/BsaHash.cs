using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Canonical serialization + SHA-256, shared by every hash this WP produces (preflight-config hash,
    /// MP-config hash placeholder, mission-unchanged checksum). Object hashes are computed from a
    /// recursively key-sorted JSON form so they are stable across machines regardless of dictionary
    /// insertion order or thread culture - never hash raw file bytes for logical-equality purposes,
    /// since this repo has no .gitattributes and line-ending handling isn't normalized across clones.
    /// Named and shaped so a future WP2 pass can adopt this rather than replace it (see AD-03).
    /// </summary>
    public static class BsaHash
    {
        public static string ComputeSha256Hex(string text)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty)));
        }

        public static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(bytes));
        }

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return ToHex(sha.ComputeHash(stream));
        }

        public static string CanonicalizeToJson(object value)
        {
            var token = value as JToken ?? JToken.FromObject(value);
            return Canonicalize(token).ToString(Formatting.None);
        }

        public static string HashObject(object value)
        {
            return ComputeSha256Hex(CanonicalizeToJson(value));
        }

        static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static JToken Canonicalize(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var sorted = new JObject();
                    foreach (var prop in ((JObject)token).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                        sorted.Add(prop.Name, Canonicalize(prop.Value));
                    return sorted;

                case JTokenType.Array:
                    return new JArray(((JArray)token).Select(Canonicalize));

                default:
                    return token.DeepClone();
            }
        }
    }
}
