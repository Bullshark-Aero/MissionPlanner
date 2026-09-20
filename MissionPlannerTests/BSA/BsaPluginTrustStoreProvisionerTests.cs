using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaPluginTrustStoreProvisionerTests
    {
        string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "bsmp-trust-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [TestMethod]
        public void MissingUserStore_IsSeededFromShippedDefault()
        {
            var shipped = Path.Combine(_root, "plugin-trust.default.json");
            var user = Path.Combine(_root, "user", "plugin-trust.json");
            File.WriteAllText(shipped, "{\"Keys\":[{\"KeyId\":\"publisher\"}]}");

            var resolved = BsaPluginTrustStoreProvisioner.ProvisionFromShippedDefault(shipped, user);

            Assert.AreEqual(user, resolved);
            Assert.AreEqual(File.ReadAllText(shipped), File.ReadAllText(user));
        }

        [TestMethod]
        public void ExistingUserStore_IsNeverOverwritten()
        {
            var shipped = Path.Combine(_root, "plugin-trust.default.json");
            var user = Path.Combine(_root, "user", "plugin-trust.json");
            Directory.CreateDirectory(Path.GetDirectoryName(user));
            File.WriteAllText(shipped, "shipped");
            File.WriteAllText(user, "operator-managed");

            BsaPluginTrustStoreProvisioner.ProvisionFromShippedDefault(shipped, user);

            Assert.AreEqual("operator-managed", File.ReadAllText(user));
        }

        [TestMethod]
        public void MissingShippedStore_FailsClearly()
        {
            var missing = Path.Combine(_root, "missing.json");
            var user = Path.Combine(_root, "user", "plugin-trust.json");

            var ex = Assert.ThrowsException<FileNotFoundException>(() =>
                BsaPluginTrustStoreProvisioner.ProvisionFromShippedDefault(missing, user));

            StringAssert.Contains(ex.Message, "shipped BSA plugin trust store is missing");
        }
    }
}
