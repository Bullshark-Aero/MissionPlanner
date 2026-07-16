using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class LockPolicyLoaderTests
    {
        const string ValidPolicy = @"{
          ""SchemaVersion"": 1,
          ""PolicyVersion"": ""1.0.0"",
          ""Default"": ""Allow"",
          ""Actions"": {
            ""ParamWrite"": [
              { ""Match"": ""AHRS_ORIENTATION"", ""Class"": ""Block"", ""InvalidatesPreflight"": true },
              { ""Match"": ""ARSPD_AUTOCAL"", ""Class"": ""Warn"", ""InvalidatesPreflight"": true }
            ],
            ""ParamResetDefaults"": { ""Class"": ""Block"" },
            ""FirmwareUpload"": { ""Class"": ""Block"" },
            ""MpSettingChange"": [
              { ""Match"": ""speechenable->false"", ""Class"": ""Warn"", ""InvalidatesPreflight"": true }
            ],
            ""MissionEdit"": { ""Class"": ""Allow"" },
            ""PreflightConfigEdit"": { ""Class"": ""Block"" },
            ""LockPolicyEdit"": { ""Class"": ""Block"" }
          }
        }";

        [TestMethod]
        public void ValidPolicy_Parses()
        {
            var config = LockPolicyLoader.Parse(ValidPolicy);
            Assert.AreEqual("1.0.0", config.PolicyVersion);
            Assert.AreEqual(LockClass.Allow, config.Default);
            Assert.AreEqual(2, config.Actions.ParamWrite.Count);
        }

        [TestMethod]
        public void MalformedJson_ThrowsLockPolicyConfigException()
        {
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse("{ not json"));
        }

        [TestMethod]
        public void UnsupportedSchemaVersion_Throws()
        {
            var json = ValidPolicy.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void MissingPolicyVersion_Throws()
        {
            var json = ValidPolicy.Replace("\"PolicyVersion\": \"1.0.0\",", "");
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void MissingDefault_Throws()
        {
            var json = ValidPolicy.Replace("\"Default\": \"Allow\",", "");
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void ParamWriteRuleMissingMatch_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""PolicyVersion"": ""1.0.0"", ""Default"": ""Allow"", ""Actions"": {
              ""ParamWrite"": [ { ""Class"": ""Block"" } ],
              ""ParamResetDefaults"": { ""Class"": ""Block"" }, ""FirmwareUpload"": { ""Class"": ""Block"" },
              ""MpSettingChange"": [], ""MissionEdit"": { ""Class"": ""Allow"" },
              ""PreflightConfigEdit"": { ""Class"": ""Block"" }, ""LockPolicyEdit"": { ""Class"": ""Block"" } } }";
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void SingleShapedAction_Missing_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""PolicyVersion"": ""1.0.0"", ""Default"": ""Allow"", ""Actions"": {
              ""ParamWrite"": [], ""FirmwareUpload"": { ""Class"": ""Block"" },
              ""MpSettingChange"": [], ""MissionEdit"": { ""Class"": ""Allow"" },
              ""PreflightConfigEdit"": { ""Class"": ""Block"" }, ""LockPolicyEdit"": { ""Class"": ""Block"" } } }";
            var ex = Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
            StringAssert.Contains(ex.Message, "ParamResetDefaults");
        }

        [TestMethod]
        public void SingleShapedAction_MissingClass_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""PolicyVersion"": ""1.0.0"", ""Default"": ""Allow"", ""Actions"": {
              ""ParamWrite"": [], ""ParamResetDefaults"": { }, ""FirmwareUpload"": { ""Class"": ""Block"" },
              ""MpSettingChange"": [], ""MissionEdit"": { ""Class"": ""Allow"" },
              ""PreflightConfigEdit"": { ""Class"": ""Block"" }, ""LockPolicyEdit"": { ""Class"": ""Block"" } } }";
            Assert.ThrowsException<LockPolicyConfigException>(() => LockPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void EmptyParamWriteList_IsValid()
        {
            var json = @"{ ""SchemaVersion"": 1, ""PolicyVersion"": ""1.0.0"", ""Default"": ""Allow"", ""Actions"": {
              ""ParamWrite"": [], ""ParamResetDefaults"": { ""Class"": ""Block"" }, ""FirmwareUpload"": { ""Class"": ""Block"" },
              ""MpSettingChange"": [], ""MissionEdit"": { ""Class"": ""Allow"" },
              ""PreflightConfigEdit"": { ""Class"": ""Block"" }, ""LockPolicyEdit"": { ""Class"": ""Block"" } } }";
            var config = LockPolicyLoader.Parse(json);
            Assert.AreEqual(0, config.Actions.ParamWrite.Count);
        }

        [TestMethod]
        public void FileNotFound_ThrowsLockPolicyConfigException()
        {
            Assert.ThrowsException<LockPolicyConfigException>(() =>
                LockPolicyLoader.Load(@"C:\this\path\definitely\does\not\exist_bsa_test.json"));
        }

        [TestMethod]
        public void ShippedDefaultPolicy_IsValid()
        {
            var path = Path.Combine(FindRepoRoot(), "BSA", "DefaultConfig", "lock_policy.default.json");
            Assert.IsTrue(File.Exists(path), $"Shipped default lock policy not found at {path}");

            var config = LockPolicyLoader.Load(path);
            Assert.IsTrue(config.Actions.ParamWrite.Count > 0);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(LockPolicyLoaderTests).Assembly.Location));
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MissionPlanner.sln")))
                dir = dir.Parent;

            if (dir == null)
                throw new DirectoryNotFoundException("Could not locate repo root (MissionPlanner.sln) from the test assembly location.");

            return dir.FullName;
        }
    }
}
