using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaConfigInstallerTests
    {
        static string TempDir() => Path.Combine(Path.GetTempPath(), "BsaConfigInstallerTests_" + Guid.NewGuid().ToString("N"));

        static ConfigPackageContents PackageWithBsaFiles() => new ConfigPackageContents
        {
            ChecklistJson = "{\"checklist\":true}",
            KeyPolicyJson = "{\"keypolicy\":true}",
            LockPolicyJson = "{\"lockpolicy\":true}"
        };

        [TestMethod]
        public void Install_AllOptedIn_WritesAllThree()
        {
            var dir = TempDir();
            try
            {
                var result = BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, true, true, true);

                Assert.AreEqual(3, result.InstalledFiles.Count);
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.ChecklistFileName)));
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.KeyPolicyFileName)));
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Install_OptOut_SkipsThatFile()
        {
            var dir = TempDir();
            try
            {
                BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, installChecklist: true, installKeyPolicy: false, installLockPolicy: false);

                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.ChecklistFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.KeyPolicyFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Install_MissingFileInPackage_NotInstalledEvenIfOptedIn()
        {
            var dir = TempDir();
            try
            {
                var package = new ConfigPackageContents { ChecklistJson = "{}" }; // no key/lock policy
                var result = BsaConfigInstaller.Install(package, dir, true, true, true);

                Assert.AreEqual(1, result.InstalledFiles.Count);
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Install_LockPolicy_IsUnstamped_AndRefusesIntegrityUntilReapproved()
        {
            var dir = TempDir();
            try
            {
                var result = BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, false, false, true);
                Assert.IsTrue(result.LockPolicyInstalledUnstamped);

                // No sidecar was written, so the WP3 integrity check must refuse to arm on this
                // foreign policy until it's re-approved in Engineering Mode.
                var lockPolicyPath = Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName);
                Assert.IsFalse(File.Exists(lockPolicyPath + ".hash"));
                Assert.IsNotNull(Lock.LockPolicyIntegrity.Verify(lockPolicyPath),
                    "An installed foreign lock policy must fail integrity (missing sidecar) until re-approved.");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Install_LockPolicy_RemovesStaleSidecar()
        {
            var dir = TempDir();
            try
            {
                Directory.CreateDirectory(dir);
                var lockPolicyPath = Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName);
                File.WriteAllText(lockPolicyPath, "{\"old\":true}");
                Lock.LockPolicyIntegrity.Stamp(lockPolicyPath); // a previously-approved policy
                Assert.IsNull(Lock.LockPolicyIntegrity.Verify(lockPolicyPath));

                BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, false, false, true);

                // The imported policy must not silently inherit the old policy's approval.
                Assert.IsFalse(File.Exists(lockPolicyPath + ".hash"));
                Assert.IsNotNull(Lock.LockPolicyIntegrity.Verify(lockPolicyPath));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
