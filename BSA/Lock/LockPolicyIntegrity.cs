using System;
using System.IO;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Stopgap tamper detection for lock_policy.json - same class of stopgap as WP2's
    /// approved_config.bsampconfig single-slot design (no real signing authority exists yet; that's
    /// still an open org decision per the WP3 doc's own DECISION NEEDED). A sidecar
    /// "lock_policy.json.hash" file records the SHA-256 of the policy at the moment it was last
    /// legitimately approved (initial seed from the shipped default, or an Engineering-Mode-saved
    /// edit). Verify() never auto-stamps a missing sidecar - a missing sidecar for a file that wasn't
    /// just legitimately seeded is exactly the tamper case this exists to catch, not something to
    /// silently treat as first approval.
    /// </summary>
    public static class LockPolicyIntegrity
    {
        public static string SidecarPath(string policyPath) => policyPath + ".hash";

        /// <summary>Call ONLY right after a legitimately-approved write (initial seed, or an
        /// Engineering-Mode-saved edit) - never from Verify() itself.</summary>
        public static void Stamp(string policyPath)
        {
            var hash = BsaHash.HashFile(policyPath);
            File.WriteAllText(SidecarPath(policyPath), hash);
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

            var actualHash = BsaHash.HashFile(policyPath);
            if (!string.Equals(actualHash, recordedHash, StringComparison.OrdinalIgnoreCase))
                return "Lock policy file has changed since it was last approved (hash mismatch) - refusing to arm until re-approved via Engineering Mode.";

            return null;
        }
    }
}
