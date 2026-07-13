using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class KeyPolicyLoaderTests
    {
        const string ValidPolicy = @"{
          ""SchemaVersion"": 1,
          ""Rules"": [ { ""Match"": ""speech*"", ""Class"": ""Portable"" } ],
          ""Default"": ""MachineSpecific""
        }";

        [TestMethod]
        public void ValidPolicy_Parses()
        {
            var config = KeyPolicyLoader.Parse(ValidPolicy);
            Assert.AreEqual(1, config.Rules.Count);
            Assert.AreEqual(KeyClass.MachineSpecific, config.Default);
        }

        [TestMethod]
        public void MalformedJson_ThrowsKeyPolicyConfigException_NotRawJsonException()
        {
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse("{ not json"));
        }

        [TestMethod]
        public void UnsupportedSchemaVersion_Throws()
        {
            var json = ValidPolicy.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void MissingDefault_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Rules"": [] }";
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void EmptyRules_IsValid_EverythingFallsToDefault()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Rules"": [], ""Default"": ""Secret"" }";
            var config = KeyPolicyLoader.Parse(json);
            Assert.AreEqual(0, config.Rules.Count);
            Assert.AreEqual(KeyClass.Secret, config.Default);
        }

        [TestMethod]
        public void RuleMissingMatch_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Rules"": [ { ""Class"": ""Portable"" } ], ""Default"": ""MachineSpecific"" }";
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void RuleMissingClass_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Rules"": [ { ""Match"": ""speech*"" } ], ""Default"": ""MachineSpecific"" }";
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void UnknownClassValue_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Rules"": [ { ""Match"": ""speech*"", ""Class"": ""SuperSecret"" } ], ""Default"": ""MachineSpecific"" }";
            Assert.ThrowsException<KeyPolicyConfigException>(() => KeyPolicyLoader.Parse(json));
        }

        [TestMethod]
        public void FileNotFound_ThrowsKeyPolicyConfigException()
        {
            Assert.ThrowsException<KeyPolicyConfigException>(() =>
                KeyPolicyLoader.Load(@"C:\this\path\definitely\does\not\exist_bsa_test.json"));
        }

        [TestMethod]
        public void ShippedDefaultPolicy_IsValid()
        {
            var path = Path.Combine(FindRepoRoot(), "BSA", "DefaultConfig", "bsa_key_policy.default.json");
            Assert.IsTrue(File.Exists(path), $"Shipped default key policy not found at {path}");

            var config = KeyPolicyLoader.Load(path);
            Assert.IsTrue(config.Rules.Count > 0);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(KeyPolicyLoaderTests).Assembly.Location));
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MissionPlanner.sln")))
                dir = dir.Parent;

            if (dir == null)
                throw new DirectoryNotFoundException("Could not locate repo root (MissionPlanner.sln) from the test assembly location.");

            return dir.FullName;
        }
    }
}
