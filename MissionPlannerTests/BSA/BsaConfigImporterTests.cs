using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaConfigImporterTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>
            {
                new KeyPolicyRule { Match = "distunits|speedunits", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "comport*", Class = KeyClass.MachineSpecific }
            },
            Default = KeyClass.MachineSpecific
        };

        static string TempJsonFile(string content = "{}")
        {
            var path = Path.Combine(Path.GetTempPath(), "BsaConfigImporterTests_src_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        static string TempPackagePath() =>
            Path.Combine(Path.GetTempPath(), "BsaConfigImporterTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");

        static string WritePackage(IReadOnlyDictionary<string, string> subset, string mpVersion = "1.3.83")
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            BsaConfigPackage.Write(outputPath, subset, checklistPath, keyPolicyPath, null,
                "1.0.0", "op", mpVersion, "");
            return outputPath;
        }

        [TestMethod]
        public void Validate_SameMajorVersion_NoWarning()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "0" }, "1.3.83");
            try
            {
                var result = BsaConfigImporter.Validate(path, "1.3.90");
                Assert.IsTrue(result.VersionCompatible);
                Assert.IsNull(result.VersionWarning);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Validate_DifferentMajorVersion_Warns_ButStillReturnsPackage()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "0" }, "1.3.83");
            try
            {
                var result = BsaConfigImporter.Validate(path, "2.0.0");
                Assert.IsFalse(result.VersionCompatible);
                StringAssert.Contains(result.VersionWarning, "1.3.83");
                StringAssert.Contains(result.VersionWarning, "2.0.0");
                Assert.IsNotNull(result.Package, "A version warning must never block validation from returning the package.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Validate_MissingOrUnparseableVersion_NoWarning()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "0" }, "");
            try
            {
                var result = BsaConfigImporter.Validate(path, "1.3.90");
                Assert.IsTrue(result.VersionCompatible, "Absence of version data must not be treated as evidence of incompatibility.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Validate_TamperedPackage_ThrowsAndReportsNoPackage()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "0" });
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var entry = archive.GetEntry(BsaConfigPackage.ConfigSubsetEntryName);
                    entry.Delete();
                    var newEntry = archive.CreateEntry(BsaConfigPackage.ConfigSubsetEntryName);
                    using (var writer = new StreamWriter(newEntry.Open()))
                        writer.Write("{\"distunits\":\"TAMPERED\"}");
                }

                Assert.ThrowsException<InvalidDataException>(() => BsaConfigImporter.Validate(path, "1.3.90"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Diff_ReturnsGroupedResult()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "1" });
            try
            {
                var package = BsaConfigPackage.Read(path);
                var live = new Dictionary<string, string> { ["distunits"] = "0" };

                var groups = BsaConfigImporter.Diff(live, package, Policy());

                Assert.AreEqual(1, groups.Count);
                CollectionAssert.Contains(groups[0].MismatchedKeys, "distunits");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Apply_OnlyApprovedKeys_PulledFromPackageValues()
        {
            var path = WritePackage(new Dictionary<string, string> { ["distunits"] = "1", ["speedunits"] = "2" });
            try
            {
                var package = BsaConfigPackage.Read(path);
                var live = new Dictionary<string, string> { ["distunits"] = "0", ["speedunits"] = "0" };

                var changed = BsaConfigImporter.Apply(live, package, new[] { "distunits" }, Policy());

                Assert.AreEqual(1, changed.Count);
                Assert.AreEqual("1", live["distunits"]);
                Assert.AreEqual("0", live["speedunits"], "Unapproved keys must not be applied even if present in the package.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Apply_NonPortableKeysInTamperedSubset_AreNeverApplied()
        {
            // BsaConfigPackage.Write takes the subset as given (classification is the EXPORTER's job),
            // so a hand-crafted/tampered package can carry non-Portable keys in its subset - "password"
            // (the app's password-protect hash) and "comport" here. Even if a caller passes them as
            // approved, Apply's belt-and-braces must refuse to write them.
            var path = WritePackage(new Dictionary<string, string>
            {
                ["distunits"] = "1",
                ["password"] = "attacker-controlled-hash",
                ["comport"] = "COM9"
            });
            try
            {
                var package = BsaConfigPackage.Read(path);
                var live = new Dictionary<string, string> { ["distunits"] = "0", ["password"] = "legitimate-hash" };

                var changed = BsaConfigImporter.Apply(live, package,
                    new[] { "distunits", "password", "comport" }, Policy());

                Assert.AreEqual(1, changed.Count);
                Assert.AreEqual("1", live["distunits"]);
                Assert.AreEqual("legitimate-hash", live["password"],
                    "A Secret/default-classed key from a tampered package subset must never overwrite live config.");
                Assert.IsFalse(live.ContainsKey("comport") && live["comport"] == "COM9",
                    "A MachineSpecific-classed key from a tampered package subset must never be written.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Backup_WritesRealPackage_ReadableAfterward()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var backupsDir = Path.Combine(Path.GetTempPath(), "BsaConfigImporterTests_backups_" + Guid.NewGuid().ToString("N"));
            try
            {
                var live = new Dictionary<string, string> { ["distunits"] = "0" };
                var backupPath = BsaConfigImporter.Backup(backupsDir, live, Policy(), checklistPath, keyPolicyPath, null,
                    "1.3.83", "test-import.bsampconfig");

                Assert.IsTrue(File.Exists(backupPath));
                var readBack = BsaConfigPackage.Read(backupPath);
                Assert.AreEqual("0", readBack.ConfigSubset["distunits"]);
                StringAssert.Contains(readBack.Manifest.ReleaseNotes, "test-import.bsampconfig");
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (Directory.Exists(backupsDir)) Directory.Delete(backupsDir, true);
            }
        }

        [TestMethod]
        public void LocalSetupFlags_ReturnsOnlyMachineSpecificKeys_Sorted()
        {
            var live = new Dictionary<string, string>
            {
                ["comport"] = "COM3",
                ["distunits"] = "0",
                ["comportB"] = "COM7"
            };

            var flags = BsaConfigImporter.LocalSetupFlags(live, Policy());

            Assert.AreEqual(2, flags.Count);
            CollectionAssert.DoesNotContain(flags, "distunits");
            Assert.AreEqual("comport", flags[0]); // sorted, Ordinal
            Assert.AreEqual("comportB", flags[1]);
        }
    }
}
