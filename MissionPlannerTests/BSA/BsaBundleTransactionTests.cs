using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaBundleTransactionTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule> { new KeyPolicyRule { Match = "distunits", Class = KeyClass.Portable } },
            Default = KeyClass.MachineSpecific
        };

        static ConfigPackageContents Package(string hash = "package-hash")
        {
            var profile = Judicar2600BundleProfile.Create(new BsaQuickViewProfile
            {
                Rows = 1, Columns = 1,
                Cells = { new BsaQuickViewCell { Position = 1, SourceId = "MAV_ESC_HOT", Label = "ESC" } }
            });
            return new ConfigPackageContents
            {
                Manifest = new PackageManifest
                {
                    SchemaVersion = 2,
                    PackageId = Judicar2600BundleProfile.PackageId,
                    PackageVersion = "1.0.0"
                },
                ConfigSubset = new Dictionary<string, string> { ["distunits"] = "1" },
                QuickView = profile.QuickView,
                TelemetryBindings = profile.TelemetryBindings,
                Warnings = profile.Warnings,
                HealthRules = profile.HealthRules,
                PackageSha256 = hash,
                LockPolicyJson = "{\"policy\":true}"
            };
        }

        [TestMethod]
        public void Apply_FailureAfterFiles_RestoresSettingsFilesAndSidecar()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var bsa = Path.Combine(root, "BSA", "config");
            var transactions = Path.Combine(root, "BSA", "transactions");
            var warning = Path.Combine(root, "warnings.xml");
            var settingsFile = Path.Combine(root, "config.xml");
            var sidecar = Path.Combine(bsa, BsaConfigInstaller.LockPolicyFileName + ".hash");
            Directory.CreateDirectory(bsa);
            File.WriteAllText(warning, "original warnings");
            File.WriteAllText(settingsFile, "exact original settings bytes");
            File.WriteAllText(sidecar, "approved stamp");
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            Action save = () => File.WriteAllText(settingsFile, string.Join(";", live));
            try
            {
                Assert.ThrowsException<InvalidOperationException>(() => BsaBundleTransaction.Apply(
                    Package(), live, new[] { "distunits" }, Policy(), new List<CustomWarning>(), save, warning,
                    bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions { InstallLockPolicy = true }, settingsFile,
                    point => { if (point == "file-committed:5") throw new IOException("injected boundary failure"); }));

                Assert.AreEqual("0", live["distunits"]);
                Assert.AreEqual("original warnings", File.ReadAllText(warning));
                Assert.AreEqual("exact original settings bytes", File.ReadAllText(settingsFile));
                Assert.AreEqual("approved stamp", File.ReadAllText(sidecar));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void RestartVerification_CommitsAndExactReimportIsNoOp()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var bsa = Path.Combine(root, "BSA", "config");
            var transactions = Path.Combine(root, "BSA", "transactions");
            var warning = Path.Combine(root, "warnings.xml");
            var settingsFile = Path.Combine(root, "config.xml");
            Directory.CreateDirectory(bsa);
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            Action save = () => File.WriteAllText(settingsFile, string.Join(";", live));
            var package = Package();
            try
            {
                var first = BsaBundleTransaction.Apply(package, live, new[] { "distunits" }, Policy(),
                    new List<CustomWarning>(), save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);
                Assert.AreEqual(BsaTransactionStatus.PendingRestart, first.Status);

                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);
                var second = BsaBundleTransaction.Apply(package, live, new[] { "distunits" }, Policy(),
                    new List<CustomWarning>(), save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);

                Assert.IsTrue(second.NoChangesRequired);
                Assert.IsFalse(second.RestartRequired);
                Assert.AreEqual(first.TransactionId, second.TransactionId);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void RestartVerification_AllowsSettingsFileNormalizationWhenImportedValuesMatch()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var bsa = Path.Combine(root, "BSA", "config");
            var transactions = Path.Combine(root, "BSA", "transactions");
            var warning = Path.Combine(root, "warnings.xml");
            var settingsFile = Path.Combine(root, "config.xml");
            Directory.CreateDirectory(bsa);
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            Action save = () => File.WriteAllText(settingsFile, string.Join(";", live));
            try
            {
                var result = BsaBundleTransaction.Apply(Package(), live, new[] { "distunits" }, Policy(),
                    new List<CustomWarning>(), save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);
                Assert.AreEqual(BsaTransactionStatus.PendingRestart, result.Status);

                File.WriteAllText(settingsFile, "normalized-by-mission-planner");
                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);

                var journal = Newtonsoft.Json.JsonConvert.DeserializeObject<BsaTransactionJournal>(
                    File.ReadAllText(Path.Combine(result.TransactionDirectory, "journal.json")));
                Assert.AreEqual(BsaTransactionStatus.Committed, journal.Status);
                Assert.AreEqual("1", live["distunits"]);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void RestartVerification_RollsBackWhenImportedValueChangedBeforeRestart()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var bsa = Path.Combine(root, "BSA", "config");
            var transactions = Path.Combine(root, "BSA", "transactions");
            var warning = Path.Combine(root, "warnings.xml");
            var settingsFile = Path.Combine(root, "config.xml");
            Directory.CreateDirectory(bsa);
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            Action save = () => File.WriteAllText(settingsFile, string.Join(";", live));
            try
            {
                var result = BsaBundleTransaction.Apply(Package(), live, new[] { "distunits" }, Policy(),
                    new List<CustomWarning>(), save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);
                live["distunits"] = "2";

                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);

                var journal = Newtonsoft.Json.JsonConvert.DeserializeObject<BsaTransactionJournal>(
                    File.ReadAllText(Path.Combine(result.TransactionDirectory, "journal.json")));
                Assert.AreEqual(BsaTransactionStatus.RolledBack, journal.Status);
                StringAssert.Contains(journal.Failure, "distunits");
                Assert.AreEqual("0", live["distunits"]);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
