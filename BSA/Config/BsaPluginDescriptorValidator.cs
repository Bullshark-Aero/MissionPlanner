using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MissionPlanner.BSA.Config
{
    public static class BsaPluginDescriptorValidator
    {
        static readonly Regex SafePluginId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);

        public static void Validate(string packagePath, ConfigPackageContents package)
        {
            var payloads = package.Manifest.Components.Where(c => c.Type == "plugin-payload").ToList();
            if (payloads.Count == 0 && package.Plugins.Count == 0) return;
            if (payloads.Count == 0) throw new InvalidDataException("A plugin descriptor has no executable payload.");
            if (package.Plugins.Count == 0) throw new InvalidDataException("Executable payload has no plugin descriptor.");

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                foreach (var descriptor in package.Plugins)
                {
                    if (descriptor == null || !SafePluginId.IsMatch(descriptor.PluginId ?? string.Empty) ||
                        string.IsNullOrWhiteSpace(descriptor.EntryType))
                        throw new InvalidDataException("Plugin descriptor identity and entry type are required.");

                    var payload = payloads.SingleOrDefault(c => c.Path == descriptor.PayloadPath);
                    if (payload == null || descriptor.PayloadSha256 != payload.Sha256)
                        throw new InvalidDataException("Plugin descriptor does not match its declared payload.");

                    if ((descriptor.Capabilities ?? new List<string>()).Any(capability =>
                            !(payload.Capabilities ?? new List<string>()).Contains(capability, StringComparer.Ordinal)))
                        throw new InvalidDataException("Plugin descriptor requests a capability not declared by its payload component.");

                    if ((descriptor.ProducedFieldIds ?? new List<string>()).Any(field =>
                            string.IsNullOrWhiteSpace(field) || field.StartsWith("customfield", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Plugin output fields must use stable identifiers.");

                    if (descriptor.Compatibility == null ||
                        !Version.TryParse(descriptor.Compatibility.MinimumBsmpVersion, out _) ||
                        !string.IsNullOrWhiteSpace(descriptor.Compatibility.MaximumBsmpVersionExclusive) &&
                        !Version.TryParse(descriptor.Compatibility.MaximumBsmpVersionExclusive, out _))
                        throw new InvalidDataException("Plugin compatibility metadata is invalid.");

                    if (!descriptor.PayloadPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Only precompiled DLL plugin payloads are permitted.");

                    var entry = archive.GetEntry(descriptor.PayloadPath)
                                ?? throw new InvalidDataException("Plugin payload is missing.");
                    var temp = Path.Combine(Path.GetTempPath(), "bsmp-plugin-" + Guid.NewGuid().ToString("N") + ".dll");
                    try
                    {
                        using (var input = entry.Open())
                        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                        var assemblyName = AssemblyName.GetAssemblyName(temp);
                        if (string.IsNullOrWhiteSpace(assemblyName.Name))
                            throw new InvalidDataException("Plugin assembly metadata is invalid.");
                    }
                    catch (BadImageFormatException ex)
                    {
                        throw new InvalidDataException("Plugin payload is not a valid managed assembly.", ex);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
        }
    }
}
