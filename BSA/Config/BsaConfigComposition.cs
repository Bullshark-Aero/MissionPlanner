using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Composition root: the one place WP2 code reaches for real Mission Planner globals
    /// (Settings.config, Settings.GetRunningDirectory) to assemble a real export - mirrors
    /// BsaPreflightComposition.cs. Everything it wires together stays constructor/parameter-injected and
    /// independently testable elsewhere in BSA/Config.
    /// </summary>
    public static class BsaConfigComposition
    {
        const string DefaultKeyPolicyRelativePath = "BSA\\DefaultConfig\\bsa_key_policy.default.json";
        const string UserKeyPolicyFileName = "bsa_key_policy.json";
        const string LockPolicyFileName = "lock_policy.json";

        /// <summary>
        /// Returns the path to the user's editable key policy, seeding it from the shipped default on
        /// first run - same seed-on-first-use pattern as BsaPreflightComposition.ResolveChecklistPath().
        /// </summary>
        public static string ResolveKeyPolicyPath()
        {
            var userPath = Path.Combine(BsaPaths.ConfigDirectory, UserKeyPolicyFileName);
            if (!File.Exists(userPath))
            {
                var shippedPath = Path.Combine(Settings.GetRunningDirectory(), DefaultKeyPolicyRelativePath);
                Directory.CreateDirectory(BsaPaths.ConfigDirectory);
                File.Copy(shippedPath, userPath);
            }
            return userPath;
        }

        /// <summary>Null if WP3 (which owns lock_policy.json) hasn't been built/configured yet - a
        /// missing lock policy is gracefully omitted from the package, never faked.</summary>
        static string LockPolicyPathIfPresent()
        {
            var path = Path.Combine(BsaPaths.ConfigDirectory, LockPolicyFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>Hash of the live, normalized (Portable-only) config - the "MP config hash" recorded
        /// in every WP1 report (see PreflightWizardForm.EnsureFinished). Never throws: a report must
        /// still be written even if hash computation itself fails for some reason (e.g. a corrupt key
        /// policy file), so a failure here degrades to an explanatory sentinel, not a lost report.</summary>
        public static string ComputeLiveConfigHash()
        {
            try
            {
                _ = Settings.Instance;
                var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
                var normalized = ConfigCompareEngine.Normalize(Settings.config, policy);
                return BsaHash.HashObject(normalized);
            }
            catch (Exception ex)
            {
                return $"unavailable: {ex.Message}";
            }
        }

        public static PackageManifest ExportNow(string outputPath, string operatorName, string version, string releaseNotes)
        {
            _ = Settings.Instance; // ensure Settings.config has been lazy-loaded from disk
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());

            return BsaConfigExporter.Export(
                outputPath,
                Settings.config,
                policy,
                BsaPreflightComposition.ResolveChecklistPath(),
                ResolveKeyPolicyPath(),
                LockPolicyPathIfPresent(),
                version,
                operatorName,
                Application.ProductVersion,
                releaseNotes);
        }

        // ----- WP2 Phase B: import -----

        public static ImportValidationResult ValidateImport(string packagePath) =>
            BsaConfigImporter.Validate(packagePath, Application.ProductVersion);

        public static List<ConfigDiffGroup> DiffImport(ConfigPackageContents package)
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            return BsaConfigImporter.Diff(Settings.config, package, policy);
        }

        /// <summary>Backs up the CURRENT live config before any import touches it - always call
        /// before ApplyImport().</summary>
        public static string BackupBeforeImport(string sourceDescription)
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            return BsaConfigImporter.Backup(
                BsaPaths.BackupsDirectory,
                Settings.config,
                policy,
                BsaPreflightComposition.ResolveChecklistPath(),
                ResolveKeyPolicyPath(),
                LockPolicyPathIfPresent(),
                Application.ProductVersion,
                sourceDescription);
        }

        /// <summary>Applies approved keys to the real live Settings.config and persists via a
        /// retry-wrapped Save() - see SaveWithRetry's doc comment for why the retry exists.</summary>
        public static List<string> ApplyImport(ConfigPackageContents package, IEnumerable<string> approvedKeys)
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            var changed = BsaConfigImporter.Apply(Settings.config, package, approvedKeys, policy);
            SaveWithRetry();
            return changed;
        }

        /// <summary>Read-only view of the live config for UI display (the diff preview's before/after
        /// values) - keeps Settings-global access at this composition root, per convention.</summary>
        public static IReadOnlyDictionary<string, string> LiveConfigView()
        {
            _ = Settings.Instance;
            return Settings.config;
        }

        public static List<string> LocalSetupFlagsAfterImport()
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            return BsaConfigImporter.LocalSetupFlags(Settings.config, policy);
        }

        /// <summary>
        /// Settings.config (ExtLibs/Utilities/Settings.cs) has no locking anywhere, and background
        /// writers already exist in this codebase (e.g. Utilities/AirMarket.cs calls
        /// Settings.Instance.Save() from a resumed async continuation, not the UI thread) - so
        /// Save()'s dictionary enumeration can race a concurrent write and throw
        /// InvalidOperationException. This does not eliminate that race (there is no lock in Settings
        /// to build on without touching a shared low-level file), it only makes an already-rare
        /// collision non-fatal for the import's own Save() call.
        /// </summary>
        static void SaveWithRetry()
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Settings.Instance.Save();
                    return;
                }
                catch (InvalidOperationException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
