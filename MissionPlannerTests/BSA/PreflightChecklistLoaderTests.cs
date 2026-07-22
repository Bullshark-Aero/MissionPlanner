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
        public void DeclaredGroups_EveryCheckMustDeclareAKnownGroup()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""A"", ""B""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "has no Group");
        }

        [TestMethod]
        public void DeclaredGroups_UnknownGroupName_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""A"", ""B""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""C"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "not in Metadata.Groups");
        }

        [TestMethod]
        public void DeclaredGroups_ValidGroupName_Parses()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""A"", ""B""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""A"" } ]
            }";
            var config = PreflightChecklistLoader.Parse(json);
            Assert.AreEqual("A", config.Checks[0].Group);
        }

        [TestMethod]
        public void NoDeclaredGroups_CheckSetsGroupAnyway_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"" },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""A"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "Metadata.Groups is not declared");
        }

        [TestMethod]
        public void NoDeclaredGroups_NoCheckSetsGroup_Parses()
        {
            // Back-compat: an ungrouped v1 checklist (no check declares Group) must keep loading -
            // PreflightPagePlan treats it as one implicit group.
            var config = PreflightChecklistLoader.Parse(ValidChecklist);
            Assert.IsNull(config.Checks[0].Group);
        }

        [TestMethod]
        public void DuplicateGroupName_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""A"", ""A""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""A"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "duplicate group name");
        }

        [TestMethod]
        public void BlankGroupName_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""A"", ""   ""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""A"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "blank group name");
        }

        [TestMethod]
        public void AutoGroupTitleCollidesWithDeclaredGroup_Throws()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""System checks"", ""B""] },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""System checks"" } ]
            }";
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "collides with Metadata.AutoGroupTitle");
        }

        [TestMethod]
        public void AutoGroupTitleCollision_DoesNotApply_WhenAutoChecksFirstIsFalse()
        {
            var json = @"{
              ""SchemaVersion"": 1,
              ""Metadata"": { ""Name"": ""x"", ""Groups"": [""System checks""], ""AutoChecksFirst"": false },
              ""Checks"": [ { ""Id"": ""c1"", ""Title"": ""C1"", ""Type"": ""Manual"", ""Severity"": ""Critical"", ""Instruction"": ""x"", ""Group"": ""System checks"" } ]
            }";
            var config = PreflightChecklistLoader.Parse(json);
            Assert.AreEqual(1, config.Checks.Count);
        }

        [TestMethod]
        public void PageSizeLessThanOne_Throws()
        {
            var json = ValidChecklist.Replace("\"ConfigVersion\": \"1.0.0\"", "\"ConfigVersion\": \"1.0.0\", \"PageSize\": 0");
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "PageSize must be at least 1");
        }

        [TestMethod]
        public void AutoPageSizeLessThanOne_Throws()
        {
            var json = ValidChecklist.Replace("\"ConfigVersion\": \"1.0.0\"", "\"ConfigVersion\": \"1.0.0\", \"AutoPageSize\": -1");
            var ex = Assert.ThrowsException<PreflightConfigException>(() => PreflightChecklistLoader.Parse(json));
            StringAssert.Contains(ex.Message, "AutoPageSize must be at least 1");
        }

        [TestMethod]
        public void Metadata_DefaultsApplyWhenNotSpecified()
        {
            var config = PreflightChecklistLoader.Parse(ValidChecklist);
            Assert.AreEqual(5, config.Metadata.PageSize);
            Assert.AreEqual(12, config.Metadata.AutoPageSize);
            Assert.IsTrue(config.Metadata.AutoChecksFirst);
            Assert.AreEqual("System checks", config.Metadata.AutoGroupTitle);
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
            registry.Register(MpConfigApprovedPackageCheck.Create(
                () => new MissionPlanner.BSA.Config.KeyPolicyConfig { SchemaVersion = 1, Default = MissionPlanner.BSA.Config.KeyClass.MachineSpecific },
                () => null));

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
