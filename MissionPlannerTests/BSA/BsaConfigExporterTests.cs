using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>Covers WP2 task A6 ("seed a config with fake secret keys/params/missions, assert none
    /// leak into a package"). Aircraft params and missions are excluded by construction - BsaConfigExporter's
    /// signature takes only a Settings-style string dictionary, never MAV.param/MAV.wps, so there is
    /// nothing to seed or assert for that half of the requirement; these tests focus on the half that
    /// does need runtime verification: secret-classed Settings keys.</summary>
    [TestClass]
    public class BsaConfigExporterTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>
            {
                new KeyPolicyRule { Match = "speech*|distunits", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "*password*|*apikey*|*token*", Class = KeyClass.Secret },
                new KeyPolicyRule { Match = "comport*", Class = KeyClass.MachineSpecific }
            },
            Default = KeyClass.MachineSpecific
        };

        static string TempJsonFile(string content = "{}")
        {
            var path = Path.Combine(Path.GetTempPath(), "BsaConfigExporterTests_src_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        static string TempPackagePath() =>
            Path.Combine(Path.GetTempPath(), "BsaConfigExporterTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");

        [TestMethod]
        public void Export_OnlyIncludesPortableKeys()
        {
            var live = new Dictionary<string, string>
            {
                ["distunits"] = "0",
                ["speechenable"] = "True",
                ["comport"] = "COM3",
                ["AirMarket_password"] = "hunter2-secret-value",
                ["GoogleApiKey"] = "AIzaSy-secret-value"
            };
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigExporter.Export(outputPath, live, Policy(), checklistPath, keyPolicyPath, null,
                    "1.0.0", "op", "1.3.80", "notes");

                var read = BsaConfigPackage.Read(outputPath);
                Assert.AreEqual(2, read.ConfigSubset.Count);
                Assert.IsTrue(read.ConfigSubset.ContainsKey("distunits"));
                Assert.IsTrue(read.ConfigSubset.ContainsKey("speechenable"));
                Assert.IsFalse(read.ConfigSubset.ContainsKey("comport"));
                Assert.IsFalse(read.ConfigSubset.ContainsKey("AirMarket_password"));
                Assert.IsFalse(read.ConfigSubset.ContainsKey("GoogleApiKey"));
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void Export_SecretValues_NeverAppearInAnyPackageEntry()
        {
            const string secretValue1 = "hunter2-secret-zzz1";
            const string secretValue2 = "AIzaSy-secret-zzz2";
            var live = new Dictionary<string, string>
            {
                ["distunits"] = "0",
                ["AirMarket_password"] = secretValue1,
                ["GoogleApiKey"] = secretValue2
            };
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigExporter.Export(outputPath, live, Policy(), checklistPath, keyPolicyPath, null,
                    "1.0.0", "op", "1.3.80", "notes");

                using (var archive = ZipFile.OpenRead(outputPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var text = reader.ReadToEnd();
                            Assert.IsFalse(text.Contains(secretValue1), $"Secret value leaked into {entry.FullName}");
                            Assert.IsFalse(text.Contains(secretValue2), $"Secret value leaked into {entry.FullName}");
                            Assert.IsFalse(text.Contains("AirMarket_password"), $"Secret key name leaked into {entry.FullName}");
                            Assert.IsFalse(text.Contains("GoogleApiKey"), $"Secret key name leaked into {entry.FullName}");
                        }
                    }
                }
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void Export_EmptyLiveConfig_ProducesEmptySubset()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigExporter.Export(outputPath, new Dictionary<string, string>(), Policy(),
                    checklistPath, keyPolicyPath, null, "1.0.0", "op", "1.3.80", "");
                Assert.AreEqual(0, BsaConfigPackage.Read(outputPath).ConfigSubset.Count);
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
    }
}
