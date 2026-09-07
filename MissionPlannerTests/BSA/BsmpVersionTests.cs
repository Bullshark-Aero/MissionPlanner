using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsmpVersionTests
    {
        [DataTestMethod]
        [DataRow("1.0.6-preview.1", "1.0.6-preview.2", -1)]
        [DataRow("1.0.6-preview.2", "1.0.6-preview.10", -1)]
        [DataRow("1.0.6-preview.2", "1.0.6", -1)]
        [DataRow("1.0.6+sha.123", "1.0.6+sha.456", 0)]
        [DataRow("1.0.6-preview.2+abc", "1.0.6-preview.2", 0)]
        [DataRow("1.0.6", "1.3.83", -1)]
        [DataRow("1.3.83.0", "1.3.83", 0)]
        [DataRow("1.3.83.1", "1.3.83", 1)]
        [DataRow("1.0.0-alpha", "1.0.0-alpha.1", -1)]
        [DataRow("1.0.0-1", "1.0.0-alpha", -1)]
        [DataRow("1.0.0-99999999999999999999", "1.0.0-100000000000000000000", -1)]
        public void Precedence(string left, string right, int expected)
        {
            Assert.IsTrue(BsmpVersion.TryParse(left, out var l));
            Assert.IsTrue(BsmpVersion.TryParse(right, out var r));
            Assert.AreEqual(expected, Math.Sign(l.CompareTo(r)));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("unknown")]
        [DataRow("1.0")]
        [DataRow("01.0.6")]
        [DataRow("1.0.6-preview.01")]
        [DataRow("1.0.6-")]
        [DataRow("1.0.6+")]
        [DataRow("1.0.6.0-preview.1")]
        [DataRow("1.0.6 ")]
        public void InvalidIdentityIsRejected(string value)
        {
            Assert.IsFalse(BsmpVersion.TryParse(value, out _));
        }

        [DataTestMethod]
        [DataRow("1.0.6-preview.2", "1.0.6-preview.2", true)]
        [DataRow("1.0.6-preview.2", "1.0.6-preview.10+build", true)]
        [DataRow("1.0.6-preview.2", "1.0.6", true)]
        [DataRow("1.0.6-preview.2", "1.0.6-preview.1", false)]
        [DataRow("1.0.6", "1.0.6-preview.2", false)]
        [DataRow("1.3.83", "1.0.6-preview.2", false)]
        [DataRow("1.0.6", "unknown", false)]
        public void RealBundleRoundTrip(string minimum, string running, bool accepted)
        {
            var dir = Path.Combine(Path.GetTempPath(), "BsmpVersionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var json = Path.Combine(dir, "source.json");
                File.WriteAllText(json, "{}");
                var path = Path.Combine(dir, "test.bsampconfig");
                BsaConfigPackage.Write(path, new Dictionary<string, string>(), json, json, null,
                    "1.0.0", "test", minimum, "");
                if (accepted)
                    Assert.IsTrue(BsaConfigImporter.Validate(path, running).VersionCompatible);
                else
                    Assert.ThrowsException<InvalidDataException>(() => BsaConfigImporter.Validate(path, running));
            }
            finally { Directory.Delete(dir, true); }
        }

        [DataTestMethod]
        [DataRow("1.0.6-preview.9", true)]
        [DataRow("1.0.6-preview.10", false)]
        [DataRow("1.0.6", false)]
        public void ExclusiveUpperBound(string running, bool accepted)
        {
            var dir = Path.Combine(Path.GetTempPath(), "BsmpVersionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var json = Path.Combine(dir, "source.json");
                File.WriteAllText(json, "{}");
                var path = Path.Combine(dir, "test.bsampconfig");
                BsaConfigPackage.Write(path, new Dictionary<string, string>(), json, json, null,
                    "1.0.0", "test", "1.0.6-preview.2", "");
                using (var archive = System.IO.Compression.ZipFile.Open(path,
                    System.IO.Compression.ZipArchiveMode.Update))
                {
                    var entry = archive.GetEntry("manifest.json");
                    Newtonsoft.Json.Linq.JObject manifest;
                    using (var reader = new StreamReader(entry.Open()))
                        manifest = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
                    manifest["Compatibility"]["MaximumBsmpVersionExclusive"] = "1.0.6-preview.10";
                    entry.Delete();
                    using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                        writer.Write(manifest.ToString());
                }
                if (accepted)
                    Assert.IsTrue(BsaConfigImporter.Validate(path, running).VersionCompatible);
                else
                    Assert.ThrowsException<InvalidDataException>(() => BsaConfigImporter.Validate(path, running));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
