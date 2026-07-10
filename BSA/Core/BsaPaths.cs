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
    }
}
