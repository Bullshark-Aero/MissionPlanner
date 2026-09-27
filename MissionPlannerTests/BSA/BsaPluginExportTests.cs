using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaPluginExportTests
    {
        string _root;
        string _plugins;

        [TestInitialize]
        public void Init()
        {
            _root = Path.Combine(Path.GetTempPath(), "BsaPluginExportTests_" + Guid.NewGuid().ToString("N"));
            _plugins = Path.Combine(_root, "plugins");
            Directory.CreateDirectory(_plugins);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        string ManagedDll(string directory, string name)
        {
            var path = Path.Combine(directory, name + ".dll");
            File.Copy(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location, path);
            return path;
        }

        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule> { new KeyPolicyRule { Match = "distunits", Class = KeyClass.Portable } },
            Default = KeyClass.MachineSpecific
        };

        string ExportBundle(IReadOnlyList<BsaPluginExport> plugins, bool withProfile = true)
        {
            var checklist = Path.Combine(_root, "checklist.json");
            var keyPolicy = Path.Combine(_root, "keypolicy.json");
            File.WriteAllText(checklist, "{}");
            File.WriteAllText(keyPolicy, "{}");
            var output = Path.Combine(_root, "bundle.bsampconfig");
            var profile = withProfile
                ? Judicar2600BundleProfile.Create(new BsaQuickViewProfile
                {
                    Rows = 1, Columns = 1,
                    Cells = { new BsaQuickViewCell { Position = 1, SourceId = "MAV_ESC_HOT", Label = "ESC" } }
                })
                : null;
            BsaConfigExporter.Export(output, new Dictionary<string, string> { ["distunits"] = "1" }, Policy(), checklist, keyPolicy,
                null, null, "1.0.0", "op", "1.3.83", "", profile, withProfile ? Judicar2600BundleProfile.PackageId : null, plugins);
            return output;
        }

        [TestMethod]
        public void Discover_OffersOnlyManagedDllsLoadedFromThePluginFolder()
        {
            var lights = ManagedDll(_plugins, "Judicar2600Lights");
            var outside = ManagedDll(_root, "Elsewhere");
            var badName = ManagedDll(_plugins, "bad name");

            var found = BsaPluginExport.Discover(new[]
            {
                new BsaLoadedPlugin { AssemblyPath = lights, EntryType = "BSA.Lights", Name = "Judicar 2600 Aircraft Lights", Version = "1.0.3" },
                new BsaLoadedPlugin { AssemblyPath = lights, EntryType = "BSA.LightsSecondType", Name = "Second", Version = "1.0.3" },
                new BsaLoadedPlugin { AssemblyPath = outside, EntryType = "Other", Name = "Other", Version = "1" },
                new BsaLoadedPlugin { AssemblyPath = badName, EntryType = "Bad", Name = "Bad", Version = "1" },
                new BsaLoadedPlugin { AssemblyPath = "", EntryType = "CompiledFromSource", Name = "Source", Version = "1" },
                null
            }, _plugins);

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Judicar2600Lights", found[0].PluginId);
            Assert.AreEqual("Judicar 2600 Aircraft Lights", found[0].DisplayName);
            Assert.AreEqual("1.0.3", found[0].Version);
            Assert.AreEqual("BSA.Lights", found[0].EntryType);
        }

        [TestMethod]
        public void ExportedPlugin_IsReadValidatedAndInstalledByteForByte()
        {
            var dll = ManagedDll(_plugins, "Judicar2600Lights");
            var bundle = ExportBundle(new[]
            {
                new BsaPluginExport { PluginId = "Judicar2600Lights", DisplayName = "Lights", Version = "1.0.3", EntryType = "BSA.Lights", DllPath = dll }
            });

            var read = BsaConfigPackage.Read(bundle);
            Assert.AreEqual(1, read.Plugins.Count);
            var descriptor = read.Plugins[0];
            Assert.AreEqual("Judicar2600Lights", descriptor.PluginId);
            Assert.AreEqual("1.0.3", descriptor.Version);
            Assert.AreEqual("BSA.Lights", descriptor.EntryType);
            Assert.AreEqual("plugins/Judicar2600Lights.dll", descriptor.PayloadPath);
            Assert.AreEqual(BsaHash.ComputeSha256Hex(File.ReadAllBytes(dll)), descriptor.PayloadSha256);
            BsaPluginDescriptorValidator.Validate(bundle, read);

            var target = Path.Combine(_root, "installed-plugins");
            var bsa = Path.Combine(_root, "BSA", "config");
            Directory.CreateDirectory(bsa);
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var settingsFile = Path.Combine(_root, "config.xml");
            BsaBundleTransaction.Apply(read, live, new[] { "distunits" }, Policy(),
                () => File.WriteAllText(settingsFile, string.Join(";", live)), Path.Combine(_root, "warnings.xml"),
                bsa, Path.Combine(_root, "BSA", "transactions"), target,
                new BsaBundleApplyOptions { InstallPlugins = true }, settingsFile);

            CollectionAssert.AreEqual(File.ReadAllBytes(dll), File.ReadAllBytes(Path.Combine(target, "Judicar2600Lights.dll")));
        }

        [TestMethod]
        public void Export_RefusesAPluginThatIsNotAManagedAssembly()
        {
            var fake = Path.Combine(_plugins, "NotAnAssembly.dll");
            File.WriteAllBytes(fake, new byte[] { 1, 2, 3, 4, 5 });

            Assert.ThrowsException<InvalidDataException>(() => ExportBundle(new[]
            {
                new BsaPluginExport { PluginId = "NotAnAssembly", DisplayName = "x", Version = "1", EntryType = "X", DllPath = fake }
            }));
        }

        [TestMethod]
        public void Export_WithoutAnAircraftProfile_RefusesPlugins()
        {
            var dll = ManagedDll(_plugins, "Judicar2600Lights");

            Assert.ThrowsException<InvalidOperationException>(() => ExportBundle(new[]
            {
                new BsaPluginExport { PluginId = "Judicar2600Lights", DisplayName = "Lights", Version = "1.0.3", EntryType = "BSA.Lights", DllPath = dll }
            }, withProfile: false));
        }

        [TestMethod]
        public void Export_WithNoPluginsChosen_CarriesNone()
        {
            var read = BsaConfigPackage.Read(ExportBundle(new List<BsaPluginExport>()));

            Assert.AreEqual(0, read.Plugins.Count);
            Assert.IsFalse(read.Manifest.Components.Any(c => c.Type.StartsWith("plugin-", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void PluginChoiceForm_StartsWithNothingChosen_AndReportsTheChoice()
        {
            var plugins = new List<BsaPluginExport>
            {
                new BsaPluginExport { PluginId = "A", DisplayName = "A", Version = "1" },
                new BsaPluginExport { PluginId = "B", DisplayName = "B", Version = "1" }
            };
            using (var form = new BundlePluginChoiceForm(plugins))
            {
                Assert.AreEqual(0, form.SelectedPlugins.Count);
                form.Choose("B", true);
                CollectionAssert.AreEqual(new[] { "B" }, form.SelectedPlugins.Select(p => p.PluginId).ToList());
            }
        }
    }
}
