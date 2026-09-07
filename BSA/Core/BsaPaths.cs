using System.IO;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// All BSA runtime files live under Settings.GetUserDataDirectory() + "BSA\" (plan AD-05) -
    /// config/, reports/, backups/, audit/. Centralized here so the Checks composition root and the
    /// Reports writer share one definition instead of each computing their own.
    /// </summary>
    public static class BsaPaths
    {
        public static string RootDirectory => Path.Combine(Settings.GetUserDataDirectory(), "BSA");
        public static string ConfigDirectory => Path.Combine(RootDirectory, "config");
        public static string ReportsDirectory => Path.Combine(RootDirectory, "reports");
        public static string AuditDirectory => Path.Combine(RootDirectory, "audit");
        public static string BackupsDirectory => Path.Combine(RootDirectory, "backups");
        public static string TransactionsDirectory => Path.Combine(RootDirectory, "transactions");
        public static string InstallStatePath => Path.Combine(RootDirectory, "install-state.json");
        public static string ActiveHealthRulesPath => Path.Combine(ConfigDirectory, "active-health-rules.json");
        public static string WarningOwnershipPath => Path.Combine(ConfigDirectory, "warning-ownership.json");
        public static string PluginTrustStorePath => Path.Combine(ConfigDirectory, "plugin-trust.json");

        /// <summary>Single well-known slot for "this machine's approved reference config" (WP2 Phase A -
        /// no multi-version registry yet, that's Phase B). Designating a package as approved is just
        /// copying it here; MpConfigApprovedPackageCheck compares live config against whatever - if
        /// anything - currently lives at this path.</summary>
        public static string ApprovedConfigPackagePath => Path.Combine(RootDirectory, "approved_config.bsampconfig");
    }
}
