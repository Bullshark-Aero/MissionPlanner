using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.BSA.Config
{
    /// <summary>What BsaConfigInstaller.Install actually wrote, for the operator summary + tests.</summary>
    public class BsaInstallResult
    {
        public List<string> InstalledFiles { get; } = new List<string>();
        public bool LockPolicyInstalledUnstamped { get; set; }
    }

    /// <summary>
    /// Installs the BSA config files a package carries (WP1 checklist, WP2 key policy, WP3 lock policy)
    /// into a target config directory - the missing half of the "fresh laptop" workflow: WP2 Phase B's
    /// apply step only wrote the mpconfig subset, so without this a fresh laptop got the ORGANIZATION's
    /// MP settings but the SHIPPED-DEFAULT BSA config. Pure file writes over an injected directory, so
    /// it's testable without touching the real BsaPaths.ConfigDirectory.
    ///
    /// Deliberate safety properties:
    /// - The lock policy is written WITHOUT stamping it via LockPolicyIntegrity - a foreign policy file
    ///   must be re-approved in Engineering Mode before the lock will arm (LockPolicyIntegrity.Verify
    ///   fails on a missing/mismatched sidecar). This is the tamper mechanism working as designed, not
    ///   a bypass: you can't ship a locked-down policy onto a machine and have it silently take effect.
    /// - Each file is installed only when the caller opts in (installChecklist/KeyPolicy/LockPolicy),
    ///   so overwriting a machine's existing BSA config is always explicit operator consent, never the
    ///   silent reseed the checklist staleness notice exists to warn about.
    /// </summary>
    public static class BsaConfigInstaller
    {
        public const string ChecklistFileName = "preflight_checks.json";
        public const string KeyPolicyFileName = "bsa_key_policy.json";
        public const string LockPolicyFileName = "lock_policy.json";

        public static BsaInstallResult Install(ConfigPackageContents package, string configDirectory,
            bool installChecklist, bool installKeyPolicy, bool installLockPolicy)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (string.IsNullOrWhiteSpace(configDirectory)) throw new ArgumentException("configDirectory is required.", nameof(configDirectory));

            Directory.CreateDirectory(configDirectory);
            var result = new BsaInstallResult();

            if (installChecklist && package.ChecklistJson != null)
            {
                File.WriteAllText(Path.Combine(configDirectory, ChecklistFileName), package.ChecklistJson);
                result.InstalledFiles.Add(ChecklistFileName);
            }

            if (installKeyPolicy && package.KeyPolicyJson != null)
            {
                File.WriteAllText(Path.Combine(configDirectory, KeyPolicyFileName), package.KeyPolicyJson);
                result.InstalledFiles.Add(KeyPolicyFileName);
            }

            if (installLockPolicy && package.LockPolicyJson != null)
            {
                var lockPolicyPath = Path.Combine(configDirectory, LockPolicyFileName);
                File.WriteAllText(lockPolicyPath, package.LockPolicyJson);
                // Deliberately NOT stamped - see the class doc comment. Remove any stale sidecar so the
                // policy can't accidentally pass integrity against a previous policy's hash.
                var sidecar = lockPolicyPath + ".hash";
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
                result.InstalledFiles.Add(LockPolicyFileName);
                result.LockPolicyInstalledUnstamped = true;
            }

            return result;
        }
    }
}
