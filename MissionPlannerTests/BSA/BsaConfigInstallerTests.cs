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

        /// <summary>An empty but well-formed warnings.xml - the exact shape WarningEngine.SaveConfig
        /// produces for an empty list, so the installer's parse check sees real data, not a stub.</summary>
        const string EmptyWarningsXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<ArrayOfCustomWarning xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
            "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" />";

        static ConfigPackageContents PackageWithBsaFiles() => new ConfigPackageContents
        {
            ChecklistJson = "{\"checklist\":true}",
            KeyPolicyJson = "{\"keypolicy\":true}",
            LockPolicyJson = "{\"lockpolicy\":true}",
            WarningsXml = EmptyWarningsXml
        };

        [TestMethod]
        public void Install_AllOptedIn_WritesAllFour()
        {
            var dir = TempDir();
            var userDir = TempDir();
            try
            {
                var result = BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, userDir, true, true, true, true);

                Assert.AreEqual(4, result.InstalledFiles.Count);
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.ChecklistFileName)));
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.KeyPolicyFileName)));
                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName)));

                // warnings.xml is Mission Planner's own file and belongs beside config.xml in the user
                // data directory, NOT in the BSA config directory - that split is the whole point.
                Assert.IsTrue(File.Exists(Path.Combine(userDir, BsaConfigInstaller.WarningsFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.WarningsFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(userDir)) Directory.Delete(userDir, true);
            }
        }

        [TestMethod]
        public void Install_OptOut_SkipsThatFile()
        {
            var dir = TempDir();
            var userDir = TempDir();
            try
            {
                BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, userDir, installChecklist: true,
                    installKeyPolicy: false, installLockPolicy: false, installWarnings: false);

                Assert.IsTrue(File.Exists(Path.Combine(dir, BsaConfigInstaller.ChecklistFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.KeyPolicyFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, BsaConfigInstaller.LockPolicyFileName)));
                Assert.IsFalse(File.Exists(Path.Combine(userDir, BsaConfigInstaller.WarningsFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(userDir)) Directory.Delete(userDir, true);
            }
        }

        [TestMethod]
        public void Install_WarningsAbsentFromPackage_NotInstalledEvenIfOptedIn()
        {
            var dir = TempDir();
            var userDir = TempDir();
            try
            {
                var package = new ConfigPackageContents { ChecklistJson = "{}" }; // pre-warnings package
                var result = BsaConfigInstaller.Install(package, dir, userDir, true, true, true, true);

                CollectionAssert.DoesNotContain(result.InstalledFiles, BsaConfigInstaller.WarningsFileName);
                Assert.IsFalse(File.Exists(Path.Combine(userDir, BsaConfigInstaller.WarningsFileName)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(userDir)) Directory.Delete(userDir, true);
            }
        }

        [TestMethod]
        public void Install_MalformedWarnings_ThrowsAndLeavesExistingFileIntact()
        {
            var dir = TempDir();
            var userDir = TempDir();
            try
            {
                Directory.CreateDirectory(userDir);
                var warningsPath = Path.Combine(userDir, BsaConfigInstaller.WarningsFileName);
                File.WriteAllText(warningsPath, EmptyWarningsXml);

                var package = new ConfigPackageContents { WarningsXml = "<not-warnings><unclosed>" };

                // WarningEngine's static constructor swallows a parse failure and comes up empty, so an
                // unparseable file would silently wipe the operator's warnings. Refuse it up front.
                Assert.ThrowsException<InvalidDataException>(() =>
                    BsaConfigInstaller.Install(package, dir, userDir, false, false, false, true));

                Assert.AreEqual(EmptyWarningsXml, File.ReadAllText(warningsPath));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(userDir)) Directory.Delete(userDir, true);
            }
        }

        [TestMethod]
        public void Install_MissingFileInPackage_NotInstalledEvenIfOptedIn()
        {
            var dir = TempDir();
            try
            {
                var package = new ConfigPackageContents { ChecklistJson = "{}" }; // no key/lock policy
                var result = BsaConfigInstaller.Install(package, dir, null, true, true, true, false);

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
                var result = BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, null, false, false, true, false);
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

                BsaConfigInstaller.Install(PackageWithBsaFiles(), dir, null, false, false, true, false);

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
