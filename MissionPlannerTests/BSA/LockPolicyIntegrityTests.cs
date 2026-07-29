using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class LockPolicyIntegrityTests
    {
        // EngineeringMode.DerivedIntegrityKey reads the real, global Settings.config (same as
        // EngineeringModeTests) - save and restore around every test so this doesn't leak state into
        // other tests or the real user's config, and so these tests are deterministic regardless of
        // whether this machine happens to have a real Engineering passphrase configured.
        Dictionary<string, string> _originalConfig;

        [TestInitialize]
        public void SaveConfig()
        {
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

        static string TempPolicyFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "LockPolicyIntegrityTests_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void MissingFile_VerifyFails()
        {
            var reason = LockPolicyIntegrity.Verify(@"C:\does\not\exist_bsa_test.json");
            Assert.IsNotNull(reason);
        }

        [TestMethod]
        public void NoSidecar_VerifyFails_NeverAutoStamps()
        {
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                var reason = LockPolicyIntegrity.Verify(path);
                Assert.IsNotNull(reason, "A missing sidecar must be treated as tampered, never silently approved.");
                Assert.IsFalse(File.Exists(LockPolicyIntegrity.SidecarPath(path)),
                    "Verify() must never create a sidecar itself - only Stamp() may.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void StampedThenUnchanged_VerifySucceeds()
        {
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path);
                Assert.IsNull(LockPolicyIntegrity.Verify(path));
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        [TestMethod]
        public void StampedThenModified_VerifyFails()
        {
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path);
                File.WriteAllText(path, "{\"a\":2}"); // tamper after stamping
                var reason = LockPolicyIntegrity.Verify(path);
                Assert.IsNotNull(reason);
                StringAssert.Contains(reason, "changed");
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        [TestMethod]
        public void ReStampAfterEdit_VerifySucceedsAgain()
        {
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path);
                File.WriteAllText(path, "{\"a\":2}");
                Assert.IsNotNull(LockPolicyIntegrity.Verify(path));

                LockPolicyIntegrity.Stamp(path); // simulates an Engineering-Mode-approved re-save
                Assert.IsNull(LockPolicyIntegrity.Verify(path));
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        [TestMethod]
        public void Configured_StampThenUnchanged_VerifySucceeds()
        {
            EngineeringMode.SetPassphrase("mo");
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path);
                Assert.IsNull(LockPolicyIntegrity.Verify(path));
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        // This is the concrete fix requested: with an Engineering passphrase configured, a plain
        // content hash (what an attacker gets from certutil/Get-FileHash - the previous, unkeyed
        // scheme) must NOT satisfy Verify(). Only the HMAC keyed by the Engineering passphrase's
        // stored hash may.
        [TestMethod]
        public void Configured_PlainContentHash_DoesNotForgeSidecar()
        {
            EngineeringMode.SetPassphrase("mo");
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                File.WriteAllText(LockPolicyIntegrity.SidecarPath(path), BsaHash.HashFile(path));
                var reason = LockPolicyIntegrity.Verify(path);
                Assert.IsNotNull(reason, "An unkeyed content hash must not be accepted once an Engineering passphrase is configured.");
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        [TestMethod]
        public void StampedUnconfigured_ThenEngineeringModeConfigured_VerifyFailsClosed_UntilReStamped()
        {
            Settings.config.Remove("bsa_engineering_password_set");
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path); // bootstrap-style unkeyed stamp
                Assert.IsNull(LockPolicyIntegrity.Verify(path));

                EngineeringMode.SetPassphrase("mo"); // configured for the first time, file untouched
                Assert.IsNotNull(LockPolicyIntegrity.Verify(path),
                    "Newly configuring Engineering Mode must force one re-approval, not silently trust the old unkeyed stamp.");

                LockPolicyIntegrity.Stamp(path); // the forced re-approval
                Assert.IsNull(LockPolicyIntegrity.Verify(path));
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }

        [TestMethod]
        public void PassphraseChanged_InvalidatesPriorStamp()
        {
            EngineeringMode.SetPassphrase("mo");
            var path = TempPolicyFile("{\"a\":1}");
            try
            {
                LockPolicyIntegrity.Stamp(path);
                Assert.IsNull(LockPolicyIntegrity.Verify(path));

                EngineeringMode.SetPassphrase("different passphrase");
                Assert.IsNotNull(LockPolicyIntegrity.Verify(path),
                    "A stamp keyed to the old passphrase must not verify once the passphrase changes.");
            }
            finally
            {
                File.Delete(path);
                File.Delete(LockPolicyIntegrity.SidecarPath(path));
            }
        }
    }
}
