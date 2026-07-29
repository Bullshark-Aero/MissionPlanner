using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// Only the pure file-reading helper is testable here - the rest of the composition root touches
    /// MainV2/Settings globals by design.
    /// </summary>
    [TestClass]
    public class BsaPreflightCompositionTests
    {
        static string TempFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "BsaCompositionTests_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void ReadChecklistConfigVersion_ValidChecklist_ReturnsVersion()
        {
            var path = TempFile(@"{ ""SchemaVersion"": 1, ""Metadata"": { ""Name"": ""x"", ""ConfigVersion"": ""1.1.0"" }, ""Checks"": [] }");
            try
            {
                Assert.AreEqual(new Version(1, 1, 0), BsaPreflightComposition.ReadChecklistConfigVersion(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ReadChecklistConfigVersion_NewerBeatsOlder()
        {
            var older = TempFile(@"{ ""SchemaVersion"": 1, ""Metadata"": { ""Name"": ""x"", ""ConfigVersion"": ""1.0.0"" }, ""Checks"": [] }");
            var newer = TempFile(@"{ ""SchemaVersion"": 1, ""Metadata"": { ""Name"": ""x"", ""ConfigVersion"": ""1.1.0"" }, ""Checks"": [] }");
            try
            {
                Assert.IsTrue(BsaPreflightComposition.ReadChecklistConfigVersion(newer) >
                              BsaPreflightComposition.ReadChecklistConfigVersion(older));
            }
            finally
            {
                File.Delete(older);
                File.Delete(newer);
            }
        }

        [TestMethod]
        public void ReadChecklistConfigVersion_MissingFile_ReturnsNull()
        {
            Assert.IsNull(BsaPreflightComposition.ReadChecklistConfigVersion(@"C:\does\not\exist_bsa_test.json"));
        }

        [TestMethod]
        public void ReadChecklistConfigVersion_MalformedJsonOrVersion_ReturnsNull()
        {
            var garbage = TempFile("{ not json");
            var badVersion = TempFile(@"{ ""SchemaVersion"": 1, ""Metadata"": { ""Name"": ""x"", ""ConfigVersion"": ""banana"" }, ""Checks"": [] }");
            var noMetadata = TempFile(@"{ ""SchemaVersion"": 1, ""Checks"": [] }");
            try
            {
                Assert.IsNull(BsaPreflightComposition.ReadChecklistConfigVersion(garbage));
                Assert.IsNull(BsaPreflightComposition.ReadChecklistConfigVersion(badVersion));
                Assert.IsNull(BsaPreflightComposition.ReadChecklistConfigVersion(noMetadata));
            }
            finally
            {
                File.Delete(garbage);
                File.Delete(badVersion);
                File.Delete(noMetadata);
            }
        }
    }
}
