using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                HealthRules = profile.HealthRules,
                WarningsXml = "<ArrayOfCustomWarning />",
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
                    Package(), live, new[] { "distunits" }, Policy(), save, warning,
                    bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions { InstallLockPolicy = true, InstallWarnings = true }, settingsFile,
                    point => { if (point == "file-committed:4") throw new IOException("injected boundary failure"); }));

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
        public void Apply_QuickViewRemovesAKey_StillCompletes()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var bsa = Path.Combine(root, "BSA", "config");
            var transactions = Path.Combine(root, "BSA", "transactions");
            var warning = Path.Combine(root, "warnings.xml");
            var settingsFile = Path.Combine(root, "config.xml");
            Directory.CreateDirectory(bsa);
            var live = new Dictionary<string, string>
            {
                ["distunits"] = "0",
                ["quickView1_labelcolor"] = "Red"
            };
            Action save = () => File.WriteAllText(settingsFile, string.Join(";", live));
            try
            {
                var result = BsaBundleTransaction.Apply(Package(), live, new[] { "distunits" }, Policy(),
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);

                Assert.AreEqual(BsaTransactionStatus.PendingRestart, result.Status);
                Assert.IsFalse(live.ContainsKey("quickView1_labelcolor"), "the removal should have been applied");
                CollectionAssert.Contains(result.ChangedSettings.ToList(), "quickView1_labelcolor");
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
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions(), settingsFile);
                Assert.AreEqual(BsaTransactionStatus.PendingRestart, first.Status);

                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);
                var second = BsaBundleTransaction.Apply(package, live, new[] { "distunits" }, Policy(),
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
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
        public void Apply_PluginsAreNotInstalledUnlessOptedIn()
        {
            using (var f = PluginBundleFixture.Create())
            {
                var installed = Path.Combine(f.PluginDirectory, "aero.bullshark.test.plugin.dll");

                var withoutOptIn = f.Apply(new BsaBundleApplyOptions());
                Assert.IsFalse(withoutOptIn.PluginsInstalled);
                Assert.IsFalse(File.Exists(installed), "no plugin should be installed without an explicit yes");

                f.Reset();
                var withOptIn = f.Apply(new BsaBundleApplyOptions { InstallPlugins = true });
                Assert.IsTrue(withOptIn.PluginsInstalled);
                Assert.IsTrue(File.Exists(installed), "opting in should install the plugin");
            }
        }

        [TestMethod]
        public void Apply_PayloadSwappedAfterValidation_IsRefusedBeforeAnythingChanges()
        {
            using (var f = PluginBundleFixture.Create())
            {
                f.SwapPayloadOnDisk();

                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    f.Apply(new BsaBundleApplyOptions { InstallPlugins = true }));

                StringAssert.Contains(ex.Message, "does not match the payload this bundle was validated with");
                Assert.IsFalse(File.Exists(Path.Combine(f.PluginDirectory, "aero.bullshark.test.plugin.dll")));
                Assert.AreEqual("0", f.Live["distunits"], "nothing should have been applied");
            }
        }

        [TestMethod]
        public void Reimport_AfterDecliningPlugins_InstallsThemWhenAskedAgain()
        {
            using (var f = PluginBundleFixture.Create())
            {
                var installed = Path.Combine(f.PluginDirectory, "aero.bullshark.test.plugin.dll");

                f.Apply(new BsaBundleApplyOptions());
                f.Commit();
                Assert.IsFalse(File.Exists(installed), "precondition: declined, so not installed");

                var second = f.Apply(new BsaBundleApplyOptions { InstallPlugins = true });

                Assert.IsFalse(second.NoChangesRequired, "a requested plugin that is not there is work to do");
                Assert.IsTrue(second.PluginsInstalled);
                Assert.IsTrue(File.Exists(installed));
            }
        }

        sealed class PluginBundleFixture : IDisposable
        {
            public string Root { get; private set; }
            public string PluginDirectory { get; private set; }
            public Dictionary<string, string> Live { get; private set; }
            ConfigPackageContents _package;
            string _bundlePath, _bsa, _transactions, _warning, _settingsFile;

            public static PluginBundleFixture Create()
            {
                var root = Path.Combine(Path.GetTempPath(), "BsaPluginTx_" + Guid.NewGuid().ToString("N"));
                var f = new PluginBundleFixture
                {
                    Root = root,
                    PluginDirectory = Path.Combine(root, "plugins"),
                    _bsa = Path.Combine(root, "BSA", "config"),
                    _transactions = Path.Combine(root, "BSA", "transactions"),
                    _warning = Path.Combine(root, "warnings.xml"),
                    _settingsFile = Path.Combine(root, "config.xml"),
                    _bundlePath = Path.Combine(root, "plugin.bsampconfig"),
                    Live = new Dictionary<string, string> { ["distunits"] = "0" }
                };
                Directory.CreateDirectory(f._bsa);

                var payload = File.ReadAllBytes(typeof(BsaBundleTransactionTests).Assembly.Location);
                using (var archive = System.IO.Compression.ZipFile.Open(f._bundlePath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("plugins/test.dll", System.IO.Compression.CompressionLevel.NoCompression);
                    using (var stream = entry.Open()) stream.Write(payload, 0, payload.Length);
                }

                var basePackage = Package();
                f._package = new ConfigPackageContents
                {
                    Manifest = basePackage.Manifest,
                    ConfigSubset = basePackage.ConfigSubset,
                    QuickView = basePackage.QuickView,
                    TelemetryBindings = basePackage.TelemetryBindings,
                    HealthRules = basePackage.HealthRules,
                    PackageSha256 = "plugin-package-hash",
                    SourcePath = f._bundlePath,
                    Plugins = new List<BsaPluginDescriptor>
                    {
                        new BsaPluginDescriptor
                        {
                            PluginId = "aero.bullshark.test.plugin",
                            Version = "1.0.0",
                            EntryType = "Test.Plugin",
                            Compatibility = new PackageCompatibility { MinimumBsmpVersion = "1.3.83" },
                            PayloadPath = "plugins/test.dll",
                            PayloadSha256 = MissionPlanner.BSA.Core.BsaHash.ComputeSha256Hex(payload)
                        }
                    }
                };
                return f;
            }

            public BsaBundleApplyResult Apply(BsaBundleApplyOptions options)
            {
                Action save = () => File.WriteAllText(_settingsFile, string.Join(";", Live));
                return BsaBundleTransaction.Apply(_package, Live, new[] { "distunits" }, Policy(), save, _warning,
                    _bsa, _transactions, PluginDirectory, options, _settingsFile);
            }

            public void Commit()
            {
                Action save = () => File.WriteAllText(_settingsFile, string.Join(";", Live));
                BsaBundleTransaction.RecoverAndVerify(_transactions, Live, save);
            }

            public void Reset()
            {
                if (Directory.Exists(_transactions)) Directory.Delete(_transactions, true);
                var installState = Path.Combine(Root, "BSA", "install-state.json");
                if (File.Exists(installState)) File.Delete(installState);
                Live["distunits"] = "0";
            }

            public void SwapPayloadOnDisk()
            {
                File.Delete(_bundlePath);
                using (var archive = System.IO.Compression.ZipFile.Open(_bundlePath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("plugins/test.dll", System.IO.Compression.CompressionLevel.NoCompression);
                    using (var stream = entry.Open())
                    {
                        var other = System.Text.Encoding.UTF8.GetBytes("not the payload that was validated");
                        stream.Write(other, 0, other.Length);
                    }
                }
            }

            public void Dispose()
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }

        [TestMethod]
        public void RestartVerification_OperatorEditedWarnings_StillCommits()
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
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
                    new BsaBundleApplyOptions { InstallWarnings = true }, settingsFile);
                Assert.AreEqual(BsaTransactionStatus.PendingRestart, result.Status);
                Assert.IsTrue(result.WarningsInstalled);

                File.WriteAllText(warning, "<?xml version=\"1.0\" encoding=\"utf-8\"?><ArrayOfCustomWarning />");
                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);

                var journal = Newtonsoft.Json.JsonConvert.DeserializeObject<BsaTransactionJournal>(
                    File.ReadAllText(Path.Combine(result.TransactionDirectory, "journal.json")));
                Assert.AreEqual(BsaTransactionStatus.Committed, journal.Status, journal.Failure);
                CollectionAssert.Contains(journal.EditableTargets, warning);
                Assert.IsFalse(journal.ExpectedHashes.ContainsKey(warning));
                Assert.AreEqual("1", live["distunits"]);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void Reimport_AfterSettingDrift_RepairsTheSetting()
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
                BsaBundleTransaction.Apply(package, live, new[] { "distunits" }, Policy(), save, warning,
                    bsa, transactions, Path.Combine(root, "plugins"), new BsaBundleApplyOptions(), settingsFile);
                BsaBundleTransaction.RecoverAndVerify(transactions, live, save);
                Assert.AreEqual("1", live["distunits"], "precondition: committed");

                live["distunits"] = "0";
                var repair = BsaBundleTransaction.Apply(package, live, new[] { "distunits" }, Policy(), save, warning,
                    bsa, transactions, Path.Combine(root, "plugins"), new BsaBundleApplyOptions(), settingsFile);

                Assert.IsFalse(repair.NoChangesRequired, "a drifted setting is a change worth applying");
                Assert.AreEqual("1", live["distunits"], "the approved value should have been restored");
                CollectionAssert.Contains(repair.ChangedSettings.ToList(), "distunits");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void RecoverAndVerify_UnreadableJournal_ReportsInsteadOfThrowing()
        {
            var root = Path.Combine(Path.GetTempPath(), "BsaBundleTransactionTests_" + Guid.NewGuid().ToString("N"));
            var transactions = Path.Combine(root, "BSA", "transactions");
            Directory.CreateDirectory(Path.Combine(transactions, "20260101000000-deadbeef"));
            File.WriteAllText(Path.Combine(transactions, "20260101000000-deadbeef", "journal.json"), "{ \"Status\": ");
            try
            {
                var outcomes = BsaBundleTransaction.RecoverAndVerify(transactions, new Dictionary<string, string>(), () => { });

                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].RecoveryFailed);
                Assert.IsTrue(outcomes[0].RolledBack, "a failed recovery must reach the operator");
                StringAssert.Contains(outcomes[0].Failure, "Recovery could not complete");
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
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
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
                    save, warning, bsa, transactions, Path.Combine(root, "plugins"),
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
