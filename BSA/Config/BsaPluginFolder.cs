using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Config
{
    public sealed class BsaPluginPlan
    {
        public List<BsaPluginDescriptor> Install { get; } = new List<BsaPluginDescriptor>();
        public List<BsaPluginDescriptor> AlreadyInstalled { get; } = new List<BsaPluginDescriptor>();
        public List<string> Remove { get; } = new List<string>();
        public bool ChangesAnything => Install.Count > 0 || Remove.Count > 0;
    }

    public static class BsaPluginFolder
    {
        static readonly string[] LoaderSkips = { "microsoft.", "system.", "missionplanner.grid.dll", "usbserialforandroid" };

        public static List<string> LoadablePluginDlls(string pluginDirectory)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory)) return found;
            var full = Path.GetFullPath(pluginDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var installDirectory = Path.GetDirectoryName(full);
            foreach (var path in Directory.GetFiles(full))
            {
                var name = Path.GetFileName(path);
                if (!string.Equals(Path.GetExtension(name), ".dll", StringComparison.OrdinalIgnoreCase)) continue;
                var lower = name.ToLowerInvariant();
                if (LoaderSkips.Any(lower.Contains)) continue;
                if (installDirectory != null && File.Exists(Path.Combine(installDirectory, name))) continue;
                found.Add(path);
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        public static List<string> Unlisted(ConfigPackageContents package, string pluginDirectory)
        {
            var listed = new HashSet<string>(
                (package?.Plugins ?? new List<BsaPluginDescriptor>()).Select(p => p.PluginId + ".dll"),
                StringComparer.OrdinalIgnoreCase);
            return LoadablePluginDlls(pluginDirectory).Where(p => !listed.Contains(Path.GetFileName(p))).ToList();
        }

        public static bool IsInstalled(BsaPluginDescriptor plugin, string pluginDirectory)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(pluginDirectory)) return false;
            var target = Path.Combine(pluginDirectory, plugin.PluginId + ".dll");
            return File.Exists(target) &&
                   string.Equals(BsaHash.HashFile(target), plugin.PayloadSha256, StringComparison.OrdinalIgnoreCase);
        }

        public static BsaPluginPlan Plan(ConfigPackageContents package, string pluginDirectory)
        {
            var plan = new BsaPluginPlan();
            if (package == null || package.IsLegacy) return plan;
            foreach (var plugin in package.Plugins)
                (IsInstalled(plugin, pluginDirectory) ? plan.AlreadyInstalled : plan.Install).Add(plugin);
            plan.Remove.AddRange(Unlisted(package, pluginDirectory).Select(Path.GetFileName));
            return plan;
        }
    }
}
