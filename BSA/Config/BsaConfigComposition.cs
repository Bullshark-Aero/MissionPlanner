using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;
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
            var quickView = BsaQuickViewCodec.Export(Settings.config, CurrentState.custom_field_names);
            var profile = Judicar2600BundleProfile.Create(quickView);

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
                releaseNotes,
                profile,
                Judicar2600BundleProfile.PackageId);
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
        /// retry-wrapped Save(). A non-empty import is a configured change to the very MP config the
        /// preflight verified (WP1.6 approved-package check + the report's mpConfigHash), so it must go
        /// through the operational lock like any other settings change - CheckAction audits it while
        /// armed and Invalidate drops the lock to InvalidatedPending (both fail-open no-ops when the
        /// lock is off). Without this, Import would be a WP3 choke-point gap: an ungated settings-write
        /// surface added after WP3 enumerated its gates (README risk R4).</summary>
        public static List<string> ApplyImport(ConfigPackageContents package, IEnumerable<string> approvedKeys)
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            var changed = BsaConfigImporter.Apply(Settings.config, package, approvedKeys, policy);
            SaveWithRetry();

            if (changed.Count > 0)
            {
                BsaLockService.Instance.CheckAction("mp_setting_change", "config_import");
                BsaLockService.Instance.Invalidate("MP configuration imported while the operational lock was armed.");
            }

            return changed;
        }

        public static BsaBundleApplyResult ApplyBundleImport(ConfigPackageContents package,
            IEnumerable<string> approvedKeys, BsaBundleApplyOptions options)
        {
            _ = Settings.Instance;
            var policy = KeyPolicyLoader.Load(ResolveKeyPolicyPath());
            var result = BsaBundleTransaction.Apply(package, Settings.config, approvedKeys, policy,
                Warnings.WarningEngine.warnings, SaveWithRetry, Warnings.WarningEngine.warningconfigfile,
                BsaPaths.ConfigDirectory, BsaPaths.TransactionsDirectory,
                Path.Combine(Settings.GetRunningDirectory(), "plugins"), options,
                Path.Combine(Settings.GetUserDataDirectory(), Settings.FileName));
            BsaLockService.Instance.CheckAction("mp_setting_change", "configuration_bundle_import");
            BsaLockService.Instance.Invalidate("A BSA configuration bundle was imported while the operational lock was armed.");
            return result;
        }

        public static void RecoverBundleTransactionsAtStartup()
        {
            _ = Settings.Instance;
            BsaBundleTransaction.RecoverAndVerify(BsaPaths.TransactionsDirectory, Settings.config, SaveWithRetry);
        }

        /// <summary>Installs the BSA config files the package carries (checklist / key policy / lock
        /// policy) into the real BsaPaths.ConfigDirectory - the fresh-laptop workflow's other half. See
        /// BsaConfigInstaller for the safety properties (lock policy installed unstamped, so it must be
        /// re-approved in Engineering Mode before the lock arms).</summary>
        public static BsaInstallResult InstallBsaFilesFromPackage(ConfigPackageContents package,
            bool installChecklist, bool installKeyPolicy, bool installLockPolicy)
        {
            return BsaConfigInstaller.Install(package, BsaPaths.ConfigDirectory,
                installChecklist, installKeyPolicy, installLockPolicy);
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
