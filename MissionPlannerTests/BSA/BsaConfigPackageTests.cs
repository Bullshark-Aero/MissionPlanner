using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaConfigPackageTests
    {
        static string TempJsonFile(string content = "{}")
        {
            var path = Path.Combine(Path.GetTempPath(), "BsaConfigPackageTests_src_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        static string TempPackagePath() =>
            Path.Combine(Path.GetTempPath(), "BsaConfigPackageTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");

        [TestMethod]
        public void RoundTrip_WriteThenRead_PreservesSubsetAndManifest()
        {
            var checklistPath = TempJsonFile("{\"checklist\":true}");
            var keyPolicyPath = TempJsonFile("{\"policy\":true}");
            var outputPath = TempPackagePath();
            try
            {
                var subset = new Dictionary<string, string> { ["distunits"] = "0", ["speechenable"] = "True" };
                var written = BsaConfigPackage.Write(outputPath, subset, checklistPath, keyPolicyPath, null, null,
                    "1.2.3", "Jane Pilot", "1.3.80", "Initial export");

                Assert.AreEqual("1.2.3", written.Version);
                Assert.AreEqual("Jane Pilot", written.CreatedByOperator);
                Assert.IsTrue(written.FileHashes.ContainsKey(BsaConfigPackage.ConfigSubsetEntryName));

                var read = BsaConfigPackage.Read(outputPath);
                Assert.AreEqual("1.2.3", read.Manifest.Version);
                Assert.AreEqual("Initial export", read.Manifest.ReleaseNotes);
                Assert.AreEqual(2, read.ConfigSubset.Count);
                Assert.AreEqual("0", read.ConfigSubset["distunits"]);
                Assert.AreEqual("True", read.ConfigSubset["speechenable"]);
                Assert.IsFalse(read.HasLockPolicy);

                // bsa/ file text is extracted for the fresh-laptop install step.
                Assert.AreEqual("{\"checklist\":true}", read.ChecklistJson);
                Assert.AreEqual("{\"policy\":true}", read.KeyPolicyJson);
                Assert.IsNull(read.LockPolicyJson);
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void LockPolicy_IncludedWhenPathGiven_OmittedWhenNull()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var lockPolicyPath = TempJsonFile("{\"lock\":true}");
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(), checklistPath, keyPolicyPath,
                    lockPolicyPath, null, "1.0.0", "op", "1.3.80", "");
                var read = BsaConfigPackage.Read(outputPath);
                Assert.IsTrue(read.HasLockPolicy);
                Assert.AreEqual("{\"lock\":true}", read.LockPolicyJson);
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                File.Delete(lockPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        /// <summary>Warnings live in warnings.xml, not config.xml (WarningEngine), so before this entry
        /// existed no export could carry them and no import could restore them - the reason this was
        /// added. Carried verbatim as text, like the lock policy, and hashed into the manifest.</summary>
        [TestMethod]
        public void Warnings_IncludedWhenPathGiven_AndHashedInManifest()
        {
            const string warningsXml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ArrayOfCustomWarning />";

            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var warningsPath = TempJsonFile(warningsXml);
            var outputPath = TempPackagePath();
            try
            {
                var written = BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(), checklistPath,
                    keyPolicyPath, null, warningsPath, "1.0.0", "op", "1.3.80", "");

                Assert.IsTrue(written.FileHashes.ContainsKey(BsaConfigPackage.WarningsEntryName),
                    "The warnings entry must be hashed in the manifest like every other entry.");

                var read = BsaConfigPackage.Read(outputPath);
                Assert.IsTrue(read.HasWarnings);
                Assert.AreEqual(warningsXml, read.WarningsXml);
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                File.Delete(warningsPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        /// <summary>A machine that never created a warning has no warnings.xml, and a package written
        /// before this entry existed has no such entry either - both must read back cleanly as "no
        /// warnings offered", never as an empty warning set that would wipe the target machine's.</summary>
        [TestMethod]
        public void Warnings_OmittedWhenPathNullOrMissing_ReadsBackAsNull()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(), checklistPath, keyPolicyPath,
                    null, null, "1.0.0", "op", "1.3.80", "");
                var read = BsaConfigPackage.Read(outputPath);
                Assert.IsFalse(read.HasWarnings);
                Assert.IsNull(read.WarningsXml);

                // A path that simply doesn't exist is omitted just as gracefully - never faked.
                BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(), checklistPath, keyPolicyPath,
                    null, @"C:\does\not\exist\warnings.xml", "1.0.0", "op", "1.3.80", "");
                Assert.IsNull(BsaConfigPackage.Read(outputPath).WarningsXml);
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        /// <summary>The warnings entry is covered by the same fail-closed integrity check as the rest -
        /// a swapped-in warning set must not import silently.</summary>
        [TestMethod]
        public void TamperedWarningsEntry_FailsIntegrityCheckOnRead()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var warningsPath = TempJsonFile("<ArrayOfCustomWarning />");
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(), checklistPath, keyPolicyPath,
                    null, warningsPath, "1.0.0", "op", "1.3.80", "");

                using (var stream = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
                {
                    archive.GetEntry(BsaConfigPackage.WarningsEntryName).Delete();
                    var newEntry = archive.CreateEntry(BsaConfigPackage.WarningsEntryName);
                    using (var writer = new StreamWriter(newEntry.Open()))
                        writer.Write("<ArrayOfCustomWarning><TAMPERED /></ArrayOfCustomWarning>");
                }

                Assert.ThrowsException<InvalidDataException>(() => BsaConfigPackage.Read(outputPath));
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                File.Delete(warningsPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void TamperedEntry_FailsIntegrityCheckOnRead()
        {
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                BsaConfigPackage.Write(outputPath, new Dictionary<string, string> { ["a"] = "1" },
                    checklistPath, keyPolicyPath, null, null, "1.0.0", "op", "1.3.80", "");

                // Tamper with the config subset entry directly, bypassing the manifest's recorded hash.
                using (var stream = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
                {
                    var entry = archive.GetEntry(BsaConfigPackage.ConfigSubsetEntryName);
                    entry.Delete();
                    var newEntry = archive.CreateEntry(BsaConfigPackage.ConfigSubsetEntryName);
                    using (var writer = new StreamWriter(newEntry.Open()))
                        writer.Write("{\"a\":\"TAMPERED\"}");
                }

                Assert.ThrowsException<InvalidDataException>(() => BsaConfigPackage.Read(outputPath));
            }
            finally
            {
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void MissingChecklistFile_ThrowsFileNotFound()
        {
            var keyPolicyPath = TempJsonFile();
            var outputPath = TempPackagePath();
            try
            {
                Assert.ThrowsException<FileNotFoundException>(() =>
                    BsaConfigPackage.Write(outputPath, new Dictionary<string, string>(),
                        @"C:\does\not\exist.json", keyPolicyPath, null, null, "1.0.0", "op", "1.3.80", ""));
            }
            finally
            {
                File.Delete(keyPolicyPath);
            }
        }

        [TestMethod]
        public void ReadMissingPackage_ThrowsFileNotFound()
        {
            Assert.ThrowsException<FileNotFoundException>(() =>
                BsaConfigPackage.Read(@"C:\does\not\exist.bsampconfig"));
        }
    }
}
