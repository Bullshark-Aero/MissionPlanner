using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Builds the curated Portable-only config subset and writes it as a package via BsaConfigPackage.
    /// Takes the live config as an injected dictionary (not a direct Settings.config reference) so this
    /// is unit-testable with fakes, mirroring WP1's provider-injection pattern (TelemetryValueProvider
    /// etc.) - the real Settings.config is wired in only at BsaConfigComposition, the composition root.
    /// </summary>
    public static class BsaConfigExporter
    {
        public static PackageManifest Export(string outputPath, IReadOnlyDictionary<string, string> liveConfig,
            KeyPolicyConfig policy, string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string version, string operatorName, string missionPlannerVersion, string releaseNotes)
        {
            return Export(outputPath, liveConfig, policy, checklistJsonPath, keyPolicyJsonPath,
                lockPolicyJsonPathOrNull, version, operatorName, missionPlannerVersion, releaseNotes, null, null);
        }

        public static PackageManifest Export(string outputPath, IReadOnlyDictionary<string, string> liveConfig,
            KeyPolicyConfig policy, string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string version, string operatorName, string missionPlannerVersion, string releaseNotes,
            BsaBundleProfile profile, string packageId)
        {
            if (liveConfig == null) throw new ArgumentNullException(nameof(liveConfig));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var subset = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in liveConfig)
            {
                if (!BsaQuickViewCodec.OwnsSetting(kv.Key) &&
                    KeyClassifier.Classify(kv.Key, policy) == KeyClass.Portable)
                    subset[kv.Key] = kv.Value;
            }

            // Belt-and-braces: re-classify everything about to be written, one more time, right before
            // it leaves this machine. Guards against a future bug in the loop above (or a caller
            // mutating `subset` before Write() runs) rather than trusting a single classification pass -
            // a Secret-classed value must never reach the zip under any code path.
            foreach (var key in subset.Keys)
            {
                if (KeyClassifier.Classify(key, policy) == KeyClass.Secret)
                    throw new InvalidOperationException(
                        $"Refusing to export: key '{key}' is classified Secret and must never leave this machine.");
            }

            return BsaConfigPackage.Write(outputPath, subset, checklistJsonPath, keyPolicyJsonPath,
                lockPolicyJsonPathOrNull, version, operatorName, missionPlannerVersion, releaseNotes, profile, packageId);
        }
    }
}
