using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class MpConfigApprovedPackageCheckTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule> { new KeyPolicyRule { Match = "distunits", Class = KeyClass.Portable } },
            Default = KeyClass.MachineSpecific
        };

        static PreflightCheckDefinition Check() => new PreflightCheckDefinition { Id = "mpconfig-approved-package-match", Title = "t" };

        static string TempJsonFile(string content = "{}")
        {
            var path = Path.Combine(Path.GetTempPath(), "MpApprovedCheckTests_src_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void NoApprovedPackagePath_IsNotApplicable()
        {
            var check = MpConfigApprovedPackageCheck.Create(Policy, () => null);
            var outcome = check.Evaluate(Check(), out var detail);
            Assert.AreEqual(CheckOutcome.NotApplicable, outcome);
            StringAssert.Contains(detail, "No approved");
        }

        [TestMethod]
        public void ApprovedPackagePath_PointsToNonexistentFile_IsNotApplicable()
        {
            var check = MpConfigApprovedPackageCheck.Create(Policy, () => @"C:\does\not\exist_bsa_test.bsampconfig");
            var outcome = check.Evaluate(Check(), out _);
            Assert.AreEqual(CheckOutcome.NotApplicable, outcome);
        }

        /// <summary>
        /// ConfigCompareEngine.CompareToPackage reads the real, global Settings.config (it's the one
        /// deliberately impure entry point - see its doc comment), so exercising the Pass/Fail path here
        /// means temporarily swapping that static dictionary's contents and restoring them afterward,
        /// same pattern as BsaHashTests.StableAcrossCulture swapping and restoring the current thread's
        /// culture. Sequential test execution (no [assembly: Parallelize] in this project) makes this safe.
        /// </summary>
        [TestMethod]
        public void LiveConfigMatchesPackage_ResolvesPass()
        {
            var originalConfig = new Dictionary<string, string>(Settings.config);
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var packagePath = Path.Combine(Path.GetTempPath(), "MpApprovedCheckTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");
            try
            {
                Settings.config.Clear();
                Settings.config["distunits"] = "0";

                BsaConfigPackage.WriteLegacy(packagePath, new Dictionary<string, string> { ["distunits"] = "0" },
                    checklistPath, keyPolicyPath, null, null, "1.0.0", "op", "1.3.80", "");

                var check = MpConfigApprovedPackageCheck.Create(Policy, () => packagePath);
                var outcome = check.Evaluate(Check(), out var detail);

                Assert.AreEqual(CheckOutcome.Pass, outcome);
                StringAssert.Contains(detail, "matches");
            }
            finally
            {
                Settings.config.Clear();
                foreach (var kv in originalConfig) Settings.config[kv.Key] = kv.Value;
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(packagePath)) File.Delete(packagePath);
            }
        }

        [TestMethod]
        public void Schema2BundleFromLiveConfig_Passes_AndQuickPanelEditsDoNotFailIt()
        {
            var originalConfig = new Dictionary<string, string>(Settings.config);
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var packagePath = Path.Combine(Path.GetTempPath(), "MpApprovedCheckTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");
            KeyPolicyConfig policy() => new KeyPolicyConfig
            {
                SchemaVersion = 1,
                Rules = new List<KeyPolicyRule>
                {
                    new KeyPolicyRule { Match = "distunits", Class = KeyClass.Portable },
                    new KeyPolicyRule { Match = "quickview*", Class = KeyClass.Portable }
                },
                Default = KeyClass.MachineSpecific
            };
            try
            {
                Settings.config.Clear();
                Settings.config["distunits"] = "0";
                Settings.config["quickViewRows"] = "1";
                Settings.config["quickViewCols"] = "1";
                Settings.config["quickView1"] = "alt";
                Settings.config["quickView1_label"] = "ALT";
                Settings.config["quickViewLabel_alt"] = "ALT";

                var profile = Judicar2600BundleProfile.Create(
                    BsaQuickViewCodec.Export(Settings.config, new Dictionary<string, string>()));
                BsaConfigExporter.Export(packagePath, Settings.config, policy(), checklistPath, keyPolicyPath, null, null,
                    "1.0.0", "op", "1.3.83", "", profile, Judicar2600BundleProfile.PackageId);
                var check = MpConfigApprovedPackageCheck.Create(policy, () => packagePath);

                Assert.AreEqual(CheckOutcome.Pass, check.Evaluate(Check(), out var fresh), fresh);

                Settings.config["quickView1_label"] = "RENAMED";
                Settings.config["quickViewLabel_airspeed"] = "AS";
                Assert.AreEqual(CheckOutcome.Pass, check.Evaluate(Check(), out var edited), edited);

                Settings.config["distunits"] = "1";
                Assert.AreEqual(CheckOutcome.Fail, check.Evaluate(Check(), out var drifted));
                StringAssert.Contains(drifted, "1 changed");
            }
            finally
            {
                Settings.config.Clear();
                foreach (var kv in originalConfig) Settings.config[kv.Key] = kv.Value;
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(packagePath)) File.Delete(packagePath);
            }
        }

        [TestMethod]
        public void LiveConfigDiffersFromPackage_ResolvesFail_WithCountsInDetail()
        {
            var originalConfig = new Dictionary<string, string>(Settings.config);
            var checklistPath = TempJsonFile();
            var keyPolicyPath = TempJsonFile();
            var packagePath = Path.Combine(Path.GetTempPath(), "MpApprovedCheckTests_" + Guid.NewGuid().ToString("N") + ".bsampconfig");
            try
            {
                Settings.config.Clear();
                Settings.config["distunits"] = "1"; // live differs from the package's "0"

                BsaConfigPackage.WriteLegacy(packagePath, new Dictionary<string, string> { ["distunits"] = "0" },
                    checklistPath, keyPolicyPath, null, null, "1.0.0", "op", "1.3.80", "");

                var check = MpConfigApprovedPackageCheck.Create(Policy, () => packagePath);
                var outcome = check.Evaluate(Check(), out var detail);

                Assert.AreEqual(CheckOutcome.Fail, outcome);
                StringAssert.Contains(detail, "1 changed");
            }
            finally
            {
                Settings.config.Clear();
                foreach (var kv in originalConfig) Settings.config[kv.Key] = kv.Value;
                File.Delete(checklistPath);
                File.Delete(keyPolicyPath);
                if (File.Exists(packagePath)) File.Delete(packagePath);
            }
        }
    }
}
