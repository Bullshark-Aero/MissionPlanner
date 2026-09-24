using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MissionPlanner.BSA.Config
{
    public sealed class BsaLoadedPlugin
    {
        public string AssemblyPath { get; set; }
        public string EntryType { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
    }

    public sealed class BsaPluginExport
    {
        public string PluginId { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public string EntryType { get; set; }
        public string DllPath { get; set; }

        public static List<BsaPluginExport> Discover(IEnumerable<BsaLoadedPlugin> loaded, string pluginDirectory)
        {
            if (loaded == null || string.IsNullOrWhiteSpace(pluginDirectory)) return new List<BsaPluginExport>();
            var directory = Path.GetFullPath(pluginDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return loaded
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.AssemblyPath) && !string.IsNullOrWhiteSpace(p.EntryType))
                .Where(p => p.AssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p.AssemblyPath))
                .Where(p => string.Equals(Path.GetDirectoryName(Path.GetFullPath(p.AssemblyPath)), directory, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => Path.GetFullPath(p.AssemblyPath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(p => new BsaPluginExport
                {
                    PluginId = Path.GetFileNameWithoutExtension(p.AssemblyPath),
                    DisplayName = string.IsNullOrWhiteSpace(p.Name) ? Path.GetFileNameWithoutExtension(p.AssemblyPath) : p.Name,
                    Version = p.Version,
                    EntryType = p.EntryType,
                    DllPath = Path.GetFullPath(p.AssemblyPath)
                })
                .Where(p => BsaPluginDescriptorValidator.IsSafePluginId(p.PluginId))
                .OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
