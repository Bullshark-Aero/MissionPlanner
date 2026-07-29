using System.IO;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Composition root: the one place WP3 code reaches for real Mission Planner globals to assemble
    /// the live lock service - mirrors BsaPreflightComposition.cs / BsaConfigComposition.cs. Initialize()
    /// must run once, early in the app's lifetime (wired from MainV2's constructor, alongside the
    /// LockStatusBanner - see T6), so the BsaPreflightService.StatusChanged subscription and the
    /// BsaLockGate delegate are both live well before any preflight can possibly complete.
    /// </summary>
    public static class BsaLockComposition
    {
        const string DefaultLockPolicyRelativePath = "BSA\\DefaultConfig\\lock_policy.default.json";
        const string UserLockPolicyFileName = "lock_policy.json";

        /// <summary>Returns the path to the user's editable lock policy, seeding it from the shipped
        /// default on first run (same seed-on-first-use pattern as the WP1/WP2 composition roots) and
        /// stamping it as legitimately approved - a freshly-seeded default is provenance-known by
        /// construction, unlike a hand-copied or externally-edited file.</summary>
        public static string ResolveLockPolicyPath()
        {
            var userPath = Path.Combine(BsaPaths.ConfigDirectory, UserLockPolicyFileName);
            if (!File.Exists(userPath))
            {
                var shippedPath = Path.Combine(Settings.GetRunningDirectory(), DefaultLockPolicyRelativePath);
                Directory.CreateDirectory(BsaPaths.ConfigDirectory);
                File.Copy(shippedPath, userPath);
                LockPolicyIntegrity.Stamp(userPath);
            }
            return userPath;
        }

        /// <summary>Wires BsaLockService to the real BsaPreflightService and BsaLockGate to the real
        /// setParamAsync hook. Idempotent-safe to call more than once (re-subscribing is harmless here
        /// since MainV2 is only constructed once per process), but only ever called once in practice.</summary>
        public static void Initialize()
        {
            BsaLockService.Instance.AttachToPreflight(
                BsaPreflightService.Instance,
                ResolveLockPolicyPath,
                () => LockPolicyLoader.Load(ResolveLockPolicyPath()));

            BsaLockGate.ParamWriteCheck = BsaLockService.Instance.CheckParamWrite;
        }
    }
}
