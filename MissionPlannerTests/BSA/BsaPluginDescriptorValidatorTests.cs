using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaPluginDescriptorValidatorTests
    {
        [TestMethod]
        public void UnsignedManagedPlugin_IsAccepted()
        {
            using (var f = PluginFixture.Create())
                BsaPluginDescriptorValidator.Validate(f.BundlePath, f.Package);
        }

        [TestMethod]
        public void DataOnlyBundle_IsAccepted()
        {
            var package = new ConfigPackageContents { Manifest = new PackageManifest() };
            BsaPluginDescriptorValidator.Validate("not-opened.bsampconfig", package);
        }

        [TestMethod]
        public void PayloadThatDoesNotMatchItsDescriptor_IsRejected()
        {
            using (var f = PluginFixture.Create(descriptorHashMatches: false))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginDescriptorValidator.Validate(f.BundlePath, f.Package));
                StringAssert.Contains(ex.Message, "does not match its declared payload");
            }
        }

        [TestMethod]
        public void UnsafePluginId_IsRejected()
        {
            using (var f = PluginFixture.Create(pluginId: "../../evil"))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginDescriptorValidator.Validate(f.BundlePath, f.Package));
                StringAssert.Contains(ex.Message, "identity and entry type are required");
            }
        }

        [TestMethod]
        public void PayloadThatIsNotAManagedAssembly_IsRejected()
        {
            using (var f = PluginFixture.Create(realAssembly: false))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginDescriptorValidator.Validate(f.BundlePath, f.Package));
                StringAssert.Contains(ex.Message, "not a valid managed assembly");
            }
        }

        [TestMethod]
        public void ExecutablePayloadWithoutADescriptor_IsRejected()
        {
            var package = new ConfigPackageContents
            {
                Manifest = new PackageManifest
                {
                    Components = new List<PackageComponent>
                    {
                        new PackageComponent { Type = "plugin-payload", Path = "plugins/test.dll" }
                    }
                }
            };

            var ex = Assert.ThrowsException<InvalidDataException>(() =>
                BsaPluginDescriptorValidator.Validate("not-opened.bsampconfig", package));
            StringAssert.Contains(ex.Message, "no plugin descriptor");
        }

        sealed class PluginFixture : IDisposable
        {
            public string DirectoryPath { get; private set; }
            public string BundlePath { get; private set; }
            public ConfigPackageContents Package { get; private set; }

            public static PluginFixture Create(bool descriptorHashMatches = true, bool realAssembly = true,
                string pluginId = "aero.bullshark.test.plugin")
            {
                const string payloadPath = "plugins/test.dll";
                var fixture = new PluginFixture
                {
                    DirectoryPath = Path.Combine(Path.GetTempPath(), "bsmp-plugin-test-" + Guid.NewGuid().ToString("N"))
                };
                Directory.CreateDirectory(fixture.DirectoryPath);
                fixture.BundlePath = Path.Combine(fixture.DirectoryPath, "test.bsampconfig");

                var payload = realAssembly
                    ? File.ReadAllBytes(typeof(BsaPluginDescriptorValidatorTests).Assembly.Location)
                    : new byte[] { 0x4D, 0x5A, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04 };
                var payloadHash = BsaHash.ComputeSha256Hex(payload);

                var descriptor = new BsaPluginDescriptor
                {
                    PluginId = pluginId,
                    Version = "1.0.0",
                    EntryType = typeof(BsaPluginDescriptorValidatorTests).FullName,
                    Compatibility = new PackageCompatibility { MinimumBsmpVersion = "1.3.83" },
                    PayloadPath = payloadPath,
                    PayloadSha256 = descriptorHashMatches ? payloadHash : new string('a', 64),
                    Capabilities = new List<string> { "ui" },
                    RestartRequired = true
                };
                var manifest = new PackageManifest
                {
                    SchemaVersion = 2,
                    PackageId = "aero.bullshark.test.plugin-bundle",
                    PackageVersion = "1.0.0",
                    Compatibility = new PackageCompatibility { MinimumBsmpVersion = "1.3.83" },
                    Components = new List<PackageComponent>
                    {
                        new PackageComponent
                        {
                            ComponentId = "test-payload",
                            Type = "plugin-payload",
                            Path = payloadPath,
                            Required = true,
                            ApplyMode = "stage",
                            ByteLength = payload.LongLength,
                            Sha256 = payloadHash,
                            RestartRequired = true,
                            Capabilities = new List<string> { "ui" }
                        }
                    }
                };

                using (var archive = ZipFile.Open(fixture.BundlePath, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry(payloadPath, CompressionLevel.NoCompression);
                    using (var stream = entry.Open()) stream.Write(payload, 0, payload.Length);
                }

                fixture.Package = new ConfigPackageContents
                {
                    Manifest = manifest,
                    SourcePath = fixture.BundlePath,
                    Plugins = new List<BsaPluginDescriptor> { descriptor }
                };
                return fixture;
            }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
