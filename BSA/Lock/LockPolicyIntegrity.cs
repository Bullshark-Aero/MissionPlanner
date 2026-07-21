using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Stopgap tamper detection for lock_policy.json - same class of stopgap as WP2's
    /// approved_config.bsampconfig single-slot design (no real signing authority exists yet; that's
    /// still an open org decision per the WP3 doc's own DECISION NEEDED). A sidecar
    /// "lock_policy.json.hash" file records a hash of the policy at the moment it was last
    /// legitimately approved (initial seed from the shipped default, or an Engineering-Mode-saved
    /// edit). Verify() never auto-stamps a missing sidecar - a missing sidecar for a file that wasn't
    /// just legitimately seeded is exactly the tamper case this exists to catch, not something to
    /// silently treat as first approval.
    ///
    /// The hash is keyed (HMAC-SHA256) with EngineeringMode.DerivedIntegrityKey whenever an
    /// Engineering passphrase is configured, so producing a valid stamp requires having known that
    /// passphrase - not merely knowing this is a SHA-256 sidecar and running certutil/Get-FileHash on
    /// the policy file. It falls back to an unkeyed content hash only for the brief bootstrap window
    /// before Engineering Mode is ever configured (BsaLockComposition's seed-on-first-run stamp). The
    /// first time Engineering Mode gets configured, the next Verify() naturally fails closed against
    /// any pre-existing unkeyed stamp - that forces one re-approval which upgrades it to the keyed
    /// scheme, so no sidecar format/version marker is needed for the transition.
    ///
    /// Residual limitation (still a stopgap): the key material is the same salted-hash bytes
    /// EngineeringMode persists in Settings, which lives on the same filesystem, under the same
    /// Windows account, as the policy file itself. This raises the bar from "hash the file" to
    /// "also locate and read bsa_engineering_password out of config.xml and replicate this exact HMAC
    /// construction" - real protection against a local user with full account access still needs an
    /// externally-held signing key (the "no real signing authority yet" gap above).
    /// </summary>
    public static class LockPolicyIntegrity
    {
        public static string SidecarPath(string policyPath) => policyPath + ".hash";

        /// <summary>Call ONLY right after a legitimately-approved write (initial seed, or an
        /// Engineering-Mode-saved edit) - never from Verify() itself.</summary>
        public static void Stamp(string policyPath)
        {
            File.WriteAllText(SidecarPath(policyPath), ComputeHash(policyPath));
        }

        /// <summary>Null if untampered, else a human-readable refusal reason. A missing sidecar is
        /// treated the same as a hash mismatch - both mean this file's provenance can't be confirmed.</summary>
        public static string Verify(string policyPath)
        {
            if (string.IsNullOrWhiteSpace(policyPath) || !File.Exists(policyPath))
                return $"Lock policy not found: {policyPath}";

            var sidecarPath = SidecarPath(policyPath);
            if (!File.Exists(sidecarPath))
                return "Lock policy has no recorded approval hash (missing sidecar) - refusing to arm until re-approved via Engineering Mode.";

            string recordedHash;
            try
            {
                recordedHash = File.ReadAllText(sidecarPath).Trim();
            }
            catch (Exception ex)
            {
                return $"Could not read the lock policy's approval hash: {ex.Message}";
            }

            var actualHash = ComputeHash(policyPath);
            if (!string.Equals(actualHash, recordedHash, StringComparison.OrdinalIgnoreCase))
                return "Lock policy file has changed since it was last approved (hash mismatch) - refusing to arm until re-approved via Engineering Mode.";

            return null;
        }

        static string ComputeHash(string policyPath)
        {
            var key = EngineeringMode.DerivedIntegrityKey;
            if (key == null)
                return BsaHash.HashFile(policyPath);

            var bytes = File.ReadAllBytes(policyPath);
            using (var hmac = new HMACSHA256(key))
                return ToHex(hmac.ComputeHash(bytes));
        }

        static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
