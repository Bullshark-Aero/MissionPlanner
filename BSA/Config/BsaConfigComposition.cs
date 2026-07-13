using System;
using System.IO;
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
    }
}
