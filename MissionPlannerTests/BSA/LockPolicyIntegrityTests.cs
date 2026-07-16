using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class LockPolicyIntegrityTests
    {
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
    }
}
