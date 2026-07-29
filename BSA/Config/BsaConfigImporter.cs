using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.BSA.Config
{
    /// <summary>Result of Validate() - the package contents (already hash-verified by
    /// BsaConfigPackage.Read, which throws on tamper/corruption) plus a soft MP-version compatibility
    /// check that the doc's own risk table calls for ("import warns on major mismatch") but Phase A
    /// never implemented.</summary>
    public class ImportValidationResult
    {
        public ConfigPackageContents Package { get; set; }
        public string VersionWarning { get; set; }
        public bool VersionCompatible => VersionWarning == null;
    }

    /// <summary>
    /// Orchestrates a full import: validate, diff, backup, apply, and the post-import local-setup
    /// flag list. Each step is independently callable/testable - ImportWizardForm drives them in
    /// order, backup always running before apply. No step here calls Settings.Instance.Save() - that
    /// (plus the concurrent-write retry) is BsaConfigComposition's job, since this class stays
    /// injectable/pure like BsaConfigExporter.
    /// </summary>
    public static class BsaConfigImporter
    {
        public static ImportValidationResult Validate(string packagePath, string runningMissionPlannerVersion)
        {
            var package = BsaConfigPackage.Read(packagePath); // throws on missing/tampered/corrupted

            return new ImportValidationResult
            {
                Package = package,
                VersionWarning = CheckVersionCompatibility(package.Manifest?.MissionPlannerVersion, runningMissionPlannerVersion)
            };
        }

        /// <summary>Major-version mismatch only - never blocks, matches the doc's "import warns on
        /// major mismatch" mitigation for MP version drift. Null (no warning) if either version string
        /// is missing/unparseable - absence of data is not evidence of incompatibility.</summary>
        static string CheckVersionCompatibility(string packageVersion, string runningVersion)
        {
            var packageMajor = ExtractMajor(packageVersion);
            var runningMajor = ExtractMajor(runningVersion);

            if (packageMajor == null || runningMajor == null || packageMajor == runningMajor)
                return null;

            return $"This package was created with Mission Planner {packageVersion}, but this machine is running {runningVersion}. Some settings may not apply correctly.";
        }

        static int? ExtractMajor(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var firstDot = version.IndexOf('.');
            var majorText = firstDot >= 0 ? version.Substring(0, firstDot) : version;
            return int.TryParse(majorText, out var major) ? (int?)major : null;
        }

        public static List<ConfigDiffGroup> Diff(IReadOnlyDictionary<string, string> liveConfig,
            ConfigPackageContents package, KeyPolicyConfig policy)
        {
            var compareResult = ConfigCompareEngine.Compare(liveConfig, package.ConfigSubset, policy);
            return ConfigDiffGrouping.Group(compareResult, policy);
        }

        /// <summary>Exports the CURRENT live config as a timestamped backup - always call this before
        /// Apply(). "Restore Previous Config" is just a normal import pointed at one of these files.</summary>
        /// <returns>The backup file's full path.</returns>
        public static string Backup(string backupsDirectory, IReadOnlyDictionary<string, string> liveConfig,
            KeyPolicyConfig policy, string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string missionPlannerVersion, string sourceDescription)
        {
            Directory.CreateDirectory(backupsDirectory);
            var path = Path.Combine(backupsDirectory, $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bsampconfig");

            BsaConfigExporter.Export(path, liveConfig, policy, checklistJsonPath, keyPolicyJsonPath, lockPolicyJsonPathOrNull,
                version: "auto-backup", operatorName: "BSA Import (automatic backup)",
                missionPlannerVersion: missionPlannerVersion,
                releaseNotes: $"Automatic backup taken before importing '{sourceDescription}'.");

            return path;
        }

        /// <summary>Applies only the given keys, pulling each one's value from the package's subset -
        /// mutates liveConfig in place. Approved keys should always come from a ConfigDiffGroup's
        /// ApplicableKeys (Mismatched + PackageOnly), which are guaranteed present in the package and
        /// Portable-classified by construction; the TryGetValue and classification checks below are
        /// defensive, not real code paths for proper callers.
        ///
        /// The Portable-only filter is the import-side belt-and-braces mirroring
        /// BsaConfigExporter's Secret re-check: a package is untrusted foreign data, and a tampered
        /// subset must never be able to write a Secret or MachineSpecific key (e.g. the app's
        /// "password" hash, or "comport") into live config through any caller. Non-Portable keys are
        /// neutralized (skipped) rather than thrown on - unlike the exporter, which throws, because
        /// there the data is trusted-local and a Secret match means a local bug worth halting on,
        /// whereas here throwing would just let a garbage package abort an otherwise-valid import.</summary>
        public static List<string> Apply(IDictionary<string, string> liveConfig, ConfigPackageContents package,
            IEnumerable<string> approvedKeys, KeyPolicyConfig policy)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (approvedKeys == null) throw new ArgumentNullException(nameof(approvedKeys));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var approvedValues = new Dictionary<string, string>();
            foreach (var key in approvedKeys)
            {
                if (!package.ConfigSubset.TryGetValue(key, out var value))
                    continue;

                if (KeyClassifier.Classify(key, policy) != KeyClass.Portable)
                    continue;

                approvedValues[key] = value;
            }

            return ConfigApplier.Apply(liveConfig, approvedValues);
        }

        /// <summary>Keys currently present in the live config that classify MachineSpecific under the
        /// local policy - "not touched by this import, review before flight." Packages never carry
        /// MachineSpecific keys (only Portable ones are ever exported), so this can only report what's
        /// locally observable, not what the source machine actually had - see the WP2 Phase B plan's
        /// B5 interpretation for the full reasoning.</summary>
        public static List<string> LocalSetupFlags(IReadOnlyDictionary<string, string> liveConfig, KeyPolicyConfig policy)
        {
            var flagged = new List<string>();
            if (liveConfig == null)
                return flagged;

            foreach (var key in liveConfig.Keys)
            {
                if (KeyClassifier.Classify(key, policy) == KeyClass.MachineSpecific)
                    flagged.Add(key);
            }

            flagged.Sort(StringComparer.Ordinal);
            return flagged;
        }
    }
}
