using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Lock;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// EngineeringMode reads/writes the real, global Settings.config (like
    /// MpConfigApprovedPackageCheckTests' WP2 equivalent) - save and restore around every test so this
    /// doesn't leak state into other tests or the real user's config.
    /// </summary>
    [TestClass]
    public class EngineeringModeTests
    {
        Dictionary<string, string> _originalConfig;

        [TestInitialize]
        public void SaveConfig()
        {
            // Settings.Instance lazily loads the real config.xml on first access - force that load
            // NOW, before any test snapshots or removes keys, or a machine with a real Engineering
            // passphrase configured has the removed key silently restored mid-test by the lazy load
            // (exactly how NotConfigured_IsConfiguredIsFalse started failing once this machine's
            // passphrase was set for real).
            var _ = Settings.Instance;
            _originalConfig = new Dictionary<string, string>(Settings.config);
        }

        [TestCleanup]
        public void RestoreConfig()
        {
            Settings.config.Clear();
            foreach (var kv in _originalConfig)
                Settings.config[kv.Key] = kv.Value;
        }

        [TestMethod]
        public void NotConfigured_IsConfiguredIsFalse()
        {
            Settings.config.Remove("bsa_engineering_password_set");
            Assert.IsFalse(EngineeringMode.IsConfigured);
        }

        [TestMethod]
        public void NotConfigured_VerifyAlwaysFalse()
        {
            Settings.config.Remove("bsa_engineering_password_set");
            Assert.IsFalse(EngineeringMode.Verify("anything"));
        }

        [TestMethod]
        public void SetPassphrase_ThenVerifyCorrect_Succeeds()
        {
            EngineeringMode.SetPassphrase("correct horse battery staple");
            Assert.IsTrue(EngineeringMode.IsConfigured);
            Assert.IsTrue(EngineeringMode.Verify("correct horse battery staple"));
        }

        [TestMethod]
        public void SetPassphrase_ThenVerifyWrong_Fails()
        {
            EngineeringMode.SetPassphrase("correct horse battery staple");
            Assert.IsFalse(EngineeringMode.Verify("wrong guess"));
        }

        [TestMethod]
        public void SeparateFromAppPassword_DoesNotShareStorage()
        {
            EngineeringMode.SetPassphrase("engineering-only-secret");
            Settings.Instance["password"] = "some-unrelated-app-password-hash";
            Settings.Instance["password_protect"] = "True";

            Assert.IsTrue(EngineeringMode.Verify("engineering-only-secret"));
            Assert.AreNotEqual(Settings.Instance["password"], Settings.Instance["bsa_engineering_password"]);
        }

        [TestMethod]
        public void EmptyPassphrase_StillVerifiesConsistently()
        {
            EngineeringMode.SetPassphrase("");
            Assert.IsTrue(EngineeringMode.Verify(""));
            Assert.IsFalse(EngineeringMode.Verify("not empty"));
        }
    }
}
