using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MissionPlanner.BSA.Core;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Config
{
    public class BsaPluginTrustStore
    {
        public List<BsaTrustedPublisherKey> Keys { get; set; } = new List<BsaTrustedPublisherKey>();
    }

    public class BsaTrustedPublisherKey
    {
        public string KeyId { get; set; }
        public string ModulusBase64 { get; set; }
        public string ExponentBase64 { get; set; }
        public bool Revoked { get; set; }
    }

    /// <summary>Fail-closed verifier for future precompiled plugin components.</summary>
    public static class BsaPluginTrustValidator
    {
        static readonly Regex SafePluginId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);
        public static void Validate(string packagePath, ConfigPackageContents package, string trustStorePath)
        {
            var payloads = package.Manifest.Components.Where(c => c.Type == "plugin-payload").ToList();
            if (payloads.Count == 0) return;
            if (string.IsNullOrWhiteSpace(trustStorePath) || !File.Exists(trustStorePath))
                throw new InvalidDataException("This bundle contains executable code, but no BSA plugin trust store is provisioned.");

            var trust = JsonConvert.DeserializeObject<BsaPluginTrustStore>(File.ReadAllText(trustStorePath));
            if (trust?.Keys == null) throw new InvalidDataException("The BSA plugin trust store is invalid.");
            if (package.Plugins.Count == 0) throw new InvalidDataException("Executable payload has no plugin descriptor.");

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var signedBytes = Encoding.UTF8.GetBytes(CanonicalSignedPayload(package.Manifest));
                foreach (var signature in package.Manifest.Signatures)
                {
                    var key = trust.Keys.SingleOrDefault(k => k.KeyId == signature.KeyId && !k.Revoked);
                    if (key == null) continue;
                    var signatureEntry = archive.GetEntry(signature.Path) ?? throw new InvalidDataException("Detached signature is missing.");
                    byte[] signatureBytes;
                    using (var input = signatureEntry.Open())
                    using (var output = new MemoryStream()) { input.CopyTo(output); signatureBytes = output.ToArray(); }
                    using (var rsa = RSA.Create())
                    {
                        rsa.ImportParameters(new RSAParameters
                        {
                            Modulus = Convert.FromBase64String(key.ModulusBase64),
                            Exponent = Convert.FromBase64String(key.ExponentBase64)
                        });
                        if (!rsa.VerifyData(signedBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                            continue;
                    }
                    ValidateDescriptors(package, payloads, archive);
                    return;
                }
            }
            throw new InvalidDataException("No detached plugin signature is valid under an active BSA publisher key.");
        }

        public static string CanonicalSignedPayload(PackageManifest manifest) => BsaHash.CanonicalizeToJson(new
        {
            manifest.SchemaVersion,
            manifest.PackageId,
            manifest.PackageVersion,
            manifest.Compatibility,
            Components = manifest.Components.OrderBy(c => c.ComponentId).Select(c => new
            {
                c.ComponentId, c.Type, c.Path, c.Required, c.ApplyMode, c.ByteLength, c.Sha256,
                Dependencies = (c.Dependencies ?? new List<string>()).OrderBy(x => x),
                c.RestartRequired,
                Capabilities = (c.Capabilities ?? new List<string>()).OrderBy(x => x)
            })
        });

        static void ValidateDescriptors(ConfigPackageContents package, IReadOnlyCollection<PackageComponent> payloads, ZipArchive archive)
        {
            foreach (var descriptor in package.Plugins)
            {
                if (descriptor == null || !SafePluginId.IsMatch(descriptor.PluginId ?? string.Empty) || string.IsNullOrWhiteSpace(descriptor.EntryType))
                    throw new InvalidDataException("Plugin descriptor identity and entry type are required.");
                var payload = payloads.SingleOrDefault(c => c.Path == descriptor.PayloadPath);
                if (payload == null || descriptor.PayloadSha256 != payload.Sha256)
                    throw new InvalidDataException("Plugin descriptor does not match its declared payload.");
                if ((descriptor.Capabilities ?? new List<string>()).Any(capability =>
                        !(payload.Capabilities ?? new List<string>()).Contains(capability, StringComparer.Ordinal)))
                    throw new InvalidDataException("Plugin descriptor requests a capability not declared by its payload component.");
                if ((descriptor.ProducedFieldIds ?? new List<string>()).Any(field =>
                        string.IsNullOrWhiteSpace(field) || field.StartsWith("customfield", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Plugin output fields must use stable identifiers.");
                if (descriptor.Compatibility == null ||
                    !Version.TryParse(descriptor.Compatibility.MinimumBsmpVersion, out _) ||
                    !string.IsNullOrWhiteSpace(descriptor.Compatibility.MaximumBsmpVersionExclusive) &&
                    !Version.TryParse(descriptor.Compatibility.MaximumBsmpVersionExclusive, out _))
                    throw new InvalidDataException("Plugin compatibility metadata is invalid.");
                if (!descriptor.PayloadPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only precompiled DLL plugin payloads are permitted.");

                var entry = archive.GetEntry(descriptor.PayloadPath);
                if (entry == null) throw new InvalidDataException("Plugin payload is missing.");
                var temp = Path.Combine(Path.GetTempPath(), "bsmp-plugin-" + Guid.NewGuid().ToString("N") + ".dll");
                try
                {
                    using (var input = entry.Open())
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    var assemblyName = AssemblyName.GetAssemblyName(temp);
                    if (string.IsNullOrWhiteSpace(assemblyName.Name)) throw new InvalidDataException("Plugin assembly metadata is invalid.");
                }
                catch (BadImageFormatException ex) { throw new InvalidDataException("Plugin payload is not a valid managed assembly.", ex); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
}
