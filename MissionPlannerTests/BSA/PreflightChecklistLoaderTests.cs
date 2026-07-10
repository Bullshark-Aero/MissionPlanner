using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class PreflightChecklistLoaderTests
    {
        const string ValidChecklist = @"{
          ""SchemaVersion"": 1,
          ""Metadata"": { ""Name"": ""test"", ""ConfigVersion"": ""1.0.0"" },
          ""Checks"": [
            { ""Id"": ""c1"", ""Title"": ""Check 1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""do it"" }
          ]
        }";

        [TestMethod]
        public void ValidChecklist_Parses()
        {
            var config = PreflightChecklistLoader.Parse(ValidChecklist);
            Assert.AreEqual(1, config.Checks.Count);
            Assert.AreEqual("c1", config.Checks[0].Id);
        }

        [TestMethod]
        public void MalformedJson_ThrowsPreflightConfigException_NotRawJsonException()
        {
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse("{ not json"));
        }

        [TestMethod]
        public void UnsupportedSchemaVersion_Throws()
        {
            var json = ValidChecklist.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void EmptyChecksArray_Throws()
        {
            var json = @"{ ""SchemaVersion"": 1, ""Metadata"": { ""Name"": ""x"" }, ""Checks"": [] }";
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void DuplicateIds_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [
                { ""Id"": ""dup"", ""Title"": ""A"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"" },
                { ""Id"": ""dup"", ""Title"": ""B"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"" }
              ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "Duplicate");
        }

        [TestMethod]
        public void ManualCheck_MissingInstruction_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""A"", ""Type"": ""Manual"", ""Severity"": ""Critical"" } ]
            }";
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void AutoCheck_MissingSource_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""A"", ""Type"": ""Auto"", ""Severity"": ""Critical"" } ]
            }";
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void AutoCheck_BothFieldShapeAndCheckKey_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [
                { ""Id"": ""c1"", ""Title"": ""A"", ""Type"": ""Auto"", ""Severity"": ""Critical"", ""Source"": ""Telemetry"",
                  ""Field"": ""armed"", ""Condition"": ""EQ"", ""Value"": false, ""Check"": ""mission.nonempty"" }
              ]
            }";
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void AutoCheck_UnknownRegisteredCheckKey_ThrowsWhenKeysProvided()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [
                { ""Id"": ""c1"", ""Title"": ""A"", ""Type"": ""Auto"", ""Severity"": ""Critical"", ""Source"": ""Mission"",
                  ""Check"": ""mission.doesNotExist"" }
              ]
            }";
            Assert.ThrowsException<PreflightConfigException>(() =>
                PreflightChecklistLoader.Parse(json, new[] { "mission.nonempty" }));
        }

        [TestMethod]
        public void AutoCheck_KnownRegisteredCheckKey_Parses()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [
                { ""Id"": ""c1"", ""Title"": ""A"", ""Type"": ""Auto"", ""Severity"": ""Critical"", ""Source"": ""Mission"",
                  ""Check"": ""mission.nonempty"" }
              ]
            }";
            var config = PreflightChecklistLoader.Parse(json, new[] { "mission.nonempty" });
            Assert.AreEqual(1, config.Checks.Count);
        }

        [TestMethod]
        public void MissingType_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""A"", ""Severity"": ""Critical"" } ]
            }";
            Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
        }

        [TestMethod]
        public void FileNotFound_ThrowsPreflightConfigException()
        {
            Assert.ThrowsException<PreflightConfigException>(() =>
                PreflightChecklistLoader.Load(@"C:\this\path\definitely\does\not\exist_bsa_test.json"));
        }

        [TestMethod]
        public void ShippedDefaultChecklist_IsValid()
        {
            var path = Path.Combine(FindRepoRoot(), "BSA", "DefaultConfig", "preflight_checks.default.json");
            Assert.IsTrue(File.Exists(path), $"Shipped default checklist not found at {path}");

            var registry = new RegisteredCheckRegistry();
            foreach (var check in MissionSanityChecks.CreateAll(
                         () => new System.Collections.Generic.Dictionary<int, MissionPlanner.Utilities.Locationwp>(),
                         () => new System.Collections.Generic.Dictionary<int, MissionPlanner.Utilities.Locationwp>(),
                         () => new System.Collections.Generic.Dictionary<int, MissionPlanner.Utilities.Locationwp>(),
                         () => null,
                         null))
            {
                registry.Register(check);
            }
            registry.Register(MpConfigApprovedPackageCheck.Create());

            var config = PreflightChecklistLoader.Load(path, registry.Keys);
            Assert.IsTrue(config.Checks.Count > 0);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PreflightChecklistLoaderTests).Assembly.Location));
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MissionPlanner.sln")))
                dir = dir.Parent;

            if (dir == null)
                throw new DirectoryNotFoundException("Could not locate repo root (MissionPlanner.sln) from the test assembly location.");

            return dir.FullName;
        }
    }
}
