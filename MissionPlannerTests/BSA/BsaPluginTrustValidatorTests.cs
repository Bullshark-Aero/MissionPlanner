using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaPluginTrustValidatorTests
    {
        [TestMethod]
        public void ExecutableBundleWithoutTrustStore_FailsClosed()
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
                BsaPluginTrustValidator.Validate("not-opened.zip", package, "missing-trust-store.json"));
            StringAssert.Contains(ex.Message, "no BSA plugin trust store");
        }

        [TestMethod]
        public void SignedManagedPlugin_FromTrustedPublisher_IsAccepted()
        {
            using (var fixture = SignedPluginFixture.Create())
                BsaPluginTrustValidator.Validate(fixture.BundlePath, fixture.Package, fixture.TrustStorePath);
        }

        [TestMethod]
        public void SignedManagedPlugin_WithInvalidSignature_IsRejected()
        {
            using (var fixture = SignedPluginFixture.Create(tamperSignature: true))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginTrustValidator.Validate(fixture.BundlePath, fixture.Package, fixture.TrustStorePath));
                StringAssert.Contains(ex.Message, "No detached plugin signature is valid");
            }
        }

        [TestMethod]
        public void SignedManagedPlugin_FromUntrustedPublisher_IsRejected()
        {
            using (var fixture = SignedPluginFixture.Create(trustPublisher: false))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginTrustValidator.Validate(fixture.BundlePath, fixture.Package, fixture.TrustStorePath));
                StringAssert.Contains(ex.Message, "No detached plugin signature is valid");
            }
        }

        [TestMethod]
        public void SignedManagedPlugin_WithMismatchedDescriptorPublisher_IsRejected()
        {
            using (var fixture = SignedPluginFixture.Create(descriptorPublisherMatches: false))
            {
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                    BsaPluginTrustValidator.Validate(fixture.BundlePath, fixture.Package, fixture.TrustStorePath));
                StringAssert.Contains(ex.Message, "publisher does not match");
            }
        }

        sealed class SignedPluginFixture : IDisposable
        {
            public string DirectoryPath { get; private set; }
            public string BundlePath { get; private set; }
            public string TrustStorePath { get; private set; }
            public ConfigPackageContents Package { get; private set; }

            public static SignedPluginFixture Create(bool tamperSignature = false, bool trustPublisher = true,
                bool descriptorPublisherMatches = true)
            {
                const string keyId = "test-publisher";
                const string payloadPath = "plugins/test.dll";
                const string signaturePath = "signatures/test.sig";
                var fixture = new SignedPluginFixture
                {
                    DirectoryPath = Path.Combine(Path.GetTempPath(), "bsmp-trust-test-" + Guid.NewGuid().ToString("N"))
                };
                Directory.CreateDirectory(fixture.DirectoryPath);
                fixture.BundlePath = Path.Combine(fixture.DirectoryPath, "test.bsampconfig");
                fixture.TrustStorePath = Path.Combine(fixture.DirectoryPath, "plugin-trust.json");

                var payload = File.ReadAllBytes(typeof(BsaPluginTrustValidatorTests).Assembly.Location);
                var payloadHash = BsaHash.ComputeSha256Hex(payload);
                var descriptor = new BsaPluginDescriptor
                {
                    PluginId = "aero.bullshark.test.plugin",
                    PublisherKeyId = descriptorPublisherMatches ? keyId : "another-publisher",
                    Version = "1.0.0",
                    EntryType = typeof(BsaPluginTrustValidatorTests).FullName,
                    Compatibility = new PackageCompatibility { MinimumBsmpVersion = "1.3.83" },
                    PayloadPath = payloadPath,
                    PayloadSha256 = payloadHash,
                    Capabilities = new List<string> { "ui" },
                    RestartRequired = true
                };
                var payloadComponent = new PackageComponent
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
                };
                var manifest = new PackageManifest
                {
                    SchemaVersion = 2,
                    PackageId = "aero.bullshark.test.signed-plugin",
                    PackageVersion = "1.0.0",
                    Compatibility = new PackageCompatibility { MinimumBsmpVersion = "1.3.83" },
                    Components = new List<PackageComponent> { payloadComponent }
                };

                using (var rsa = RSA.Create())
                {
                    rsa.KeySize = 2048;
                    var signature = rsa.SignData(
                        Encoding.UTF8.GetBytes(BsaPluginTrustValidator.CanonicalSignedPayload(manifest)),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    if (tamperSignature) signature[0] ^= 0x80;
                    manifest.Signatures.Add(new PackageSignature
                    {
                        Algorithm = "RSA-SHA256",
                        KeyId = keyId,
                        Path = signaturePath,
                        ByteLength = signature.LongLength,
                        Sha256 = BsaHash.ComputeSha256Hex(signature)
                    });
                    var publicKey = rsa.ExportParameters(false);
                    var trust = new BsaPluginTrustStore
                    {
                        Keys = new List<BsaTrustedPublisherKey>
                        {
                            new BsaTrustedPublisherKey
                            {
                                KeyId = trustPublisher ? keyId : "another-publisher",
                                ModulusBase64 = Convert.ToBase64String(publicKey.Modulus),
                                ExponentBase64 = Convert.ToBase64String(publicKey.Exponent)
                            }
                        }
                    };
                    File.WriteAllText(fixture.TrustStorePath, JsonConvert.SerializeObject(trust));

                    using (var archive = ZipFile.Open(fixture.BundlePath, ZipArchiveMode.Create))
                    {
                        WriteEntry(archive, payloadPath, payload);
                        WriteEntry(archive, signaturePath, signature);
                    }
                }

                fixture.Package = new ConfigPackageContents
                {
                    Manifest = manifest,
                    Plugins = new List<BsaPluginDescriptor> { descriptor }
                };
                return fixture;
            }

            static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                using (var stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
            }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
