using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// The config package can now carry the Warnings Manager's definitions, and importing one writes
    /// warnings.xml straight over the operator's file. That only works because the composition root
    /// then calls WarningEngine.LoadConfig() to pull the new file into the running engine - without
    /// it the imported warnings are invisible until a restart AND one press of the Warnings Manager's
    /// Save button rewrites the file from the stale in-memory list, silently undoing the import.
    ///
    /// These tests pin that contract on the REAL WarningEngine (the upstream class the import depends
    /// on), not a fake. warningconfigfile is a mutable public static field, so it is redirected at a
    /// temp file here and restored in cleanup, the same purge-and-restore discipline the Settings-
    /// touching tests in this suite use.
    /// </summary>
    [TestClass]
    public class WarningsReloadContractTests
    {
        string _originalConfigFile;
        List<CustomWarning> _originalWarnings;
        string _tempFile;

        [TestInitialize]
        public void Setup()
        {
            _originalConfigFile = WarningEngine.warningconfigfile;
            _originalWarnings = WarningEngine.warnings;
            _tempFile = Path.Combine(Path.GetTempPath(), "WarningsReloadContractTests_" + Guid.NewGuid().ToString("N") + ".xml");
            WarningEngine.warningconfigfile = _tempFile;
        }

        [TestCleanup]
        public void Cleanup()
        {
            WarningEngine.warningconfigfile = _originalConfigFile;
            WarningEngine.warnings = _originalWarnings;
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [TestMethod]
        public void LoadConfig_ReplacesInMemoryWarnings_WithWhateverIsOnDisk()
        {
            WarningEngine.warnings = new List<CustomWarning>
            {
                new CustomWarning { Name = "alt", Warning = 50 }
            };

            // Stand in for an import having just written the package's warnings over the file.
            WarningEngine.warnings = new List<CustomWarning>
            {
                new CustomWarning { Name = "groundspeed", Warning = 18 },
                new CustomWarning { Name = "battery_voltage", Warning = 22 }
            };
            WarningEngine.SaveConfig();
            WarningEngine.warnings = new List<CustomWarning>
            {
                new CustomWarning { Name = "alt", Warning = 50 }
            };

            WarningEngine.LoadConfig();

            Assert.AreEqual(2, WarningEngine.warnings.Count,
                "LoadConfig must replace the in-memory list, not merge into it - this is what makes an " +
                "imported warning set take effect without a restart.");
            CollectionAssert.AreEquivalent(
                new[] { "groundspeed", "battery_voltage" },
                new List<string> { WarningEngine.warnings[0].Name, WarningEngine.warnings[1].Name });
        }

        /// <summary>Round-trips the exact file the package carries: what SaveConfig writes is what
        /// BsaConfigInstaller copies into the package and back out again, so the two must agree.</summary>
        [TestMethod]
        public void SavedFile_IsTheSameTextTheInstallerWritesAndAccepts()
        {
            WarningEngine.warnings = new List<CustomWarning>
            {
                new CustomWarning { Name = "battery_voltage", Warning = 22, color = "Red" }
            };
            WarningEngine.SaveConfig();

            var packagedText = File.ReadAllText(_tempFile);

            // Install it somewhere else, exactly as an import would, then load from there.
            var userDir = Path.Combine(Path.GetTempPath(), "WarningsReloadContractTests_user_" + Guid.NewGuid().ToString("N"));
            try
            {
                MissionPlanner.BSA.Config.BsaConfigInstaller.Install(
                    new MissionPlanner.BSA.Config.ConfigPackageContents { WarningsXml = packagedText },
                    Path.Combine(userDir, "config"), userDir,
                    installChecklist: false, installKeyPolicy: false, installLockPolicy: false, installWarnings: true);

                WarningEngine.warnings = new List<CustomWarning>();
                WarningEngine.warningconfigfile =
                    Path.Combine(userDir, MissionPlanner.BSA.Config.BsaConfigInstaller.WarningsFileName);
                WarningEngine.LoadConfig();

                Assert.AreEqual(1, WarningEngine.warnings.Count);
                Assert.AreEqual("battery_voltage", WarningEngine.warnings[0].Name);
                Assert.AreEqual(22, WarningEngine.warnings[0].Warning);
                Assert.AreEqual("Red", WarningEngine.warnings[0].color);
            }
            finally
            {
                WarningEngine.warningconfigfile = _tempFile;
                if (Directory.Exists(userDir)) Directory.Delete(userDir, true);
            }
        }

        [TestMethod]
        public void MissingFile_LeavesWarningsUntouched_RatherThanClearingThem()
        {
            WarningEngine.warnings = new List<CustomWarning> { new CustomWarning { Name = "alt" } };
            WarningEngine.warningconfigfile = Path.Combine(Path.GetTempPath(), "no_such_file_" + Guid.NewGuid().ToString("N") + ".xml");

            WarningEngine.LoadConfig();

            Assert.AreEqual(1, WarningEngine.warnings.Count);
        }
    }
}
