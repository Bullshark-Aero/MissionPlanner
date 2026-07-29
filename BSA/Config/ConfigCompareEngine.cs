using System;
using System.Collections.Generic;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Result of comparing live Settings.config against a package's config subset, over the Portable-
    /// class keys only (MachineSpecific/Secret/Volatile keys are excluded by classification before the
    /// diff even runs - a key the policy doesn't consider portable can never produce a mismatch).
    /// </summary>
    public class ConfigCompareResult
    {
        public List<string> MismatchedKeys { get; } = new List<string>();
        public List<string> LiveOnlyKeys { get; } = new List<string>();
        public List<string> PackageOnlyKeys { get; } = new List<string>();

        public bool IsMatch => MismatchedKeys.Count == 0 && LiveOnlyKeys.Count == 0 && PackageOnlyKeys.Count == 0;
    }

    /// <summary>
    /// Normalize (classify + keep Portable only + sort) then diff. This is what "MP config matches
    /// approved package" (WP1.6) and the report's MP-config hash are both built on - see
    /// BsaHash.HashObject over the same normalized form for the hash, and Compare() here for the
    /// human-readable diff.
    /// </summary>
    public static class ConfigCompareEngine
    {
        public static ConfigCompareResult Compare(IReadOnlyDictionary<string, string> live,
            IReadOnlyDictionary<string, string> package, KeyPolicyConfig policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var normalizedLive = Normalize(live, policy);
            var normalizedPackage = Normalize(package, policy);

            var result = new ConfigCompareResult();
            foreach (var kv in normalizedLive)
            {
                if (normalizedPackage.TryGetValue(kv.Key, out var packageValue))
                {
                    if (!string.Equals(kv.Value, packageValue, StringComparison.Ordinal))
                        result.MismatchedKeys.Add(kv.Key);
                }
                else
                {
                    result.LiveOnlyKeys.Add(kv.Key);
                }
            }

            foreach (var key in normalizedPackage.Keys)
            {
                if (!normalizedLive.ContainsKey(key))
                    result.PackageOnlyKeys.Add(key);
            }

            return result;
        }

        /// <summary>Convenience wrapper over the live Settings.config - the one impure entry point in
        /// this class, used by MpConfigApprovedPackageCheck. Compare() itself stays pure/testable.</summary>
        public static ConfigCompareResult CompareToPackage(string packagePath, KeyPolicyConfig policy)
        {
            var package = BsaConfigPackage.Read(packagePath);
            _ = Settings.Instance; // Settings.config is only populated once Instance has lazy-loaded it
            return Compare(Settings.config, package.ConfigSubset, policy);
        }

        /// <summary>The same normalized form used for both Compare() and the report's MP-config hash
        /// (BsaHash.HashObject over this) - both call sites must use this exact function.</summary>
        public static SortedDictionary<string, string> Normalize(IReadOnlyDictionary<string, string> source, KeyPolicyConfig policy)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (source == null)
                return result;

            foreach (var kv in source)
            {
                if (KeyClassifier.Classify(kv.Key, policy) == KeyClass.Portable)
                    result[kv.Key] = kv.Value;
            }

            return result;
        }
    }
}
