using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MissionPlanner.BSA.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MissionPlanner.BSA.Config
{
    public class ConfigPackageContents
    {
        public PackageManifest Manifest { get; set; }
        public IReadOnlyDictionary<string, string> ConfigSubset { get; set; }
        public BsaQuickViewProfile QuickView { get; set; }
        public BsaTelemetryBindings TelemetryBindings { get; set; }
        public BsaWarningProfile Warnings { get; set; }
        public BsaHealthRuleSet HealthRules { get; set; }
        public IReadOnlyList<BsaPluginDescriptor> Plugins { get; set; } = new List<BsaPluginDescriptor>();
        public string ChecklistJson { get; set; }
        public string KeyPolicyJson { get; set; }
        public string LockPolicyJson { get; set; }
        public string ReleaseNotes { get; set; }
        public bool IsLegacy { get; set; }
        public string PackageSha256 { get; set; }
        public string SourcePath { get; set; }
        public bool HasLockPolicy => LockPolicyJson != null;
        public bool HasCompleteCoreProfile => QuickView != null && TelemetryBindings != null && Warnings != null && HealthRules != null;
    }

    /// <summary>Strict schema-v2 bundle codec with an isolated reader for known schema-v1 packages.</summary>
    public static class BsaConfigPackage
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaximumEntryCount = 128;
        public const long MaximumEntryBytes = 32L * 1024 * 1024;
        public const long MaximumExpandedBytes = 128L * 1024 * 1024;
        public const double MaximumCompressionRatio = 100.0;

        public const string ManifestEntryName = "manifest.json";
        public const string ConfigSubsetEntryName = "mpconfig/config.subset.json";
        public const string QuickViewEntryName = "quickview/layout.json";
        public const string TelemetryBindingsEntryName = "telemetry/bindings.json";
        public const string WarningsEntryName = "warnings/warnings.json";
        public const string HealthRulesEntryName = "telemetry/health-rules.json";
        public const string ChecklistEntryName = "bsa/preflight_checks.json";
        const string LegacyChecklistEntryName = "bsa/preflight_checks.default.json";
        public const string KeyPolicyEntryName = "bsa/bsa_key_policy.json";
        public const string LockPolicyEntryName = "bsa/lock_policy.json";
        public const string ReleaseNotesEntryName = "RELEASE_NOTES.md";

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly HashSet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "mpconfig-subset", "quickview-layout", "telemetry-bindings", "warning-profile",
            "health-rules", "bsa-preflight-checks", "bsa-key-policy", "bsa-lock-policy",
            "release-notes", "plugin-descriptor", "plugin-payload", "signature"
        };
        static readonly HashSet<string> KnownApplyModes = new HashSet<string>(StringComparer.Ordinal)
        {
            "merge", "replace", "replace-owned", "stage", "none"
        };
        static readonly HashSet<string> KnownCapabilities = new HashSet<string>(StringComparer.Ordinal)
        {
            "telemetry-read", "virtual-fields", "ui", "file-read", "file-write", "network", "full-trust"
        };
        static readonly Dictionary<string, string> FixedPathsByType = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mpconfig-subset"] = ConfigSubsetEntryName,
            ["quickview-layout"] = QuickViewEntryName,
            ["telemetry-bindings"] = TelemetryBindingsEntryName,
            ["warning-profile"] = WarningsEntryName,
            ["health-rules"] = HealthRulesEntryName,
            ["bsa-preflight-checks"] = ChecklistEntryName,
            ["bsa-key-policy"] = KeyPolicyEntryName,
            ["bsa-lock-policy"] = LockPolicyEntryName,
            ["release-notes"] = ReleaseNotesEntryName
        };

        public static PackageManifest Write(string outputPath, IReadOnlyDictionary<string, string> subsetConfig,
            string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string version, string createdByOperator, string missionPlannerVersion, string releaseNotes)
        {
            return Write(outputPath, subsetConfig, checklistJsonPath, keyPolicyJsonPath, lockPolicyJsonPathOrNull,
                version, createdByOperator, missionPlannerVersion, releaseNotes, null, null);
        }

        public static PackageManifest Write(string outputPath, IReadOnlyDictionary<string, string> subsetConfig,
            string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string version, string createdByOperator, string missionPlannerVersion, string releaseNotes,
            BsaBundleProfile profile, string packageId)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath is required.", nameof(outputPath));
            if (subsetConfig == null) throw new ArgumentNullException(nameof(subsetConfig));
            ValidateSemVer(version, "PackageVersion");
            ValidateVersion(missionPlannerVersion, "Compatibility.MinimumBsmpVersion");

            var entries = new List<PendingEntry>();
            AddJson(entries, "mpconfig", "mpconfig-subset", ConfigSubsetEntryName, true, "merge",
                SortedCopy(subsetConfig), false);
            AddTextFile(entries, "bsa-preflight", "bsa-preflight-checks", ChecklistEntryName, false,
                "replace", checklistJsonPath, "checklist");
            AddTextFile(entries, "bsa-key-policy", "bsa-key-policy", KeyPolicyEntryName, false,
                "replace", keyPolicyJsonPath, "key policy");

            if (!string.IsNullOrWhiteSpace(lockPolicyJsonPathOrNull) && File.Exists(lockPolicyJsonPathOrNull))
                AddText(entries, "bsa-lock-policy", "bsa-lock-policy", LockPolicyEntryName, false, "replace",
                    File.ReadAllText(lockPolicyJsonPathOrNull), true);

            if (profile != null)
            {
                if (profile.QuickView == null || profile.TelemetryBindings == null || profile.Warnings == null || profile.HealthRules == null)
                    throw new InvalidDataException("A schema-v2 core profile must include QuickView, telemetry bindings, warnings, and health rules.");
                AddJson(entries, "quickview", "quickview-layout", QuickViewEntryName, true, "replace-owned", profile.QuickView, true);
                AddJson(entries, "telemetry-bindings", "telemetry-bindings", TelemetryBindingsEntryName, true, "replace-owned", profile.TelemetryBindings, true);
                AddJson(entries, "warnings", "warning-profile", WarningsEntryName, true, "replace-owned", profile.Warnings, true);
                AddJson(entries, "health-rules", "health-rules", HealthRulesEntryName, true, "replace-owned", profile.HealthRules, true);
            }

            if (releaseNotes != null)
                AddText(entries, "release-notes", "release-notes", ReleaseNotesEntryName, false, "none", releaseNotes, false);

            var manifest = new PackageManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                PackageId = string.IsNullOrWhiteSpace(packageId)
                    ? "aero.bullshark.bundle." + Guid.NewGuid().ToString("N")
                    : packageId,
                PackageVersion = version,
                CreatedByOperator = createdByOperator,
                CreatedAtUtc = DateTime.UtcNow,
                Compatibility = new PackageCompatibility { MinimumBsmpVersion = missionPlannerVersion }
            };

            foreach (var pending in entries)
            {
                pending.Component.ByteLength = pending.Bytes.LongLength;
                pending.Component.Sha256 = BsaHash.ComputeSha256Hex(pending.Bytes);
                manifest.Components.Add(pending.Component);
                manifest.FileHashes[pending.Component.Path] = pending.Component.Sha256;
            }
            manifest.MissionPlannerVersion = missionPlannerVersion;
            manifest.ReleaseNotes = releaseNotes;

            ValidateV2Manifest(manifest);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, ManifestEntryName, StrictUtf8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.Indented)));
                foreach (var pending in entries) WriteEntry(archive, pending.Component.Path, pending.Bytes);
            }
            return manifest;
        }

        public static ConfigPackageContents Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Package not found: " + path);

            try
            {
                using (var archive = ZipFile.OpenRead(path))
                {
                    var entries = ValidateArchiveEnvelope(archive);
                    if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
                        throw new InvalidDataException("Package is missing manifest.json.");
                    var manifestBytes = ReadEntryBytes(manifestEntry);
                    var manifestObject = ParseObject(manifestBytes, ManifestEntryName);
                    var schemaToken = manifestObject["SchemaVersion"];
                    var result = schemaToken == null
                        ? ReadLegacy(path, entries, manifestObject)
                        : ReadV2(path, entries, manifestObject);
                    result.PackageSha256 = BsaHash.HashFile(path);
                    result.SourcePath = Path.GetFullPath(path);
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is DecoderFallbackException)
            {
                throw new InvalidDataException("Package could not be read safely: " + ex.Message, ex);
            }
        }

        static ConfigPackageContents ReadV2(string packagePath, Dictionary<string, ZipArchiveEntry> entries, JObject manifestObject)
        {
            var manifest = DeserializeStrict<PackageManifest>(manifestObject, ManifestEntryName);
            ValidateV2Manifest(manifest);
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntryName };
            var byType = new Dictionary<string, List<PackageComponent>>(StringComparer.Ordinal);

            foreach (var component in manifest.Components)
            {
                declared.Add(component.Path);
                if (!entries.TryGetValue(component.Path, out var entry))
                {
                    if (component.Required) throw new InvalidDataException("Package is missing required component '" + component.ComponentId + "' at " + component.Path + ".");
                    continue;
                }
                var bytes = ReadEntryBytes(entry);
                if (bytes.LongLength != component.ByteLength)
                    throw new InvalidDataException("Component '" + component.ComponentId + "' has the wrong byte length.");
                var actual = BsaHash.ComputeSha256Hex(bytes);
                if (!string.Equals(actual, component.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Component '" + component.ComponentId + "' failed its SHA-256 integrity check.");
                if (!byType.TryGetValue(component.Type, out var typed))
                    byType[component.Type] = typed = new List<PackageComponent>();
                typed.Add(component);
            }

            foreach (var signature in manifest.Signatures ?? new List<PackageSignature>())
            {
                ValidateEntryPath(signature.Path);
                if (!declared.Add(signature.Path)) throw new InvalidDataException("Duplicate signature path '" + signature.Path + "'.");
                if (!entries.TryGetValue(signature.Path, out var entry)) throw new InvalidDataException("Package is missing detached signature '" + signature.Path + "'.");
                var bytes = ReadEntryBytes(entry);
                if (bytes.LongLength != signature.ByteLength || !string.Equals(BsaHash.ComputeSha256Hex(bytes), signature.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Detached signature '" + signature.Path + "' failed its integrity check.");
            }

            foreach (var path in entries.Keys)
                if (!declared.Contains(path)) throw new InvalidDataException("Package contains undeclared entry '" + path + "'.");

            var contents = new ConfigPackageContents
            {
                Manifest = manifest,
                ConfigSubset = ReadJsonComponent<Dictionary<string, string>>(entries, byType, "mpconfig-subset")
                    ?? new Dictionary<string, string>(StringComparer.Ordinal),
                QuickView = ReadJsonComponent<BsaQuickViewProfile>(entries, byType, "quickview-layout"),
                TelemetryBindings = ReadJsonComponent<BsaTelemetryBindings>(entries, byType, "telemetry-bindings"),
                Warnings = ReadJsonComponent<BsaWarningProfile>(entries, byType, "warning-profile"),
                HealthRules = ReadJsonComponent<BsaHealthRuleSet>(entries, byType, "health-rules"),
                ChecklistJson = ReadTextComponent(entries, byType, "bsa-preflight-checks"),
                KeyPolicyJson = ReadTextComponent(entries, byType, "bsa-key-policy"),
                LockPolicyJson = ReadTextComponent(entries, byType, "bsa-lock-policy"),
                ReleaseNotes = ReadTextComponent(entries, byType, "release-notes"),
                IsLegacy = false
            };
            contents.Manifest.MissionPlannerVersion = contents.Manifest.Compatibility.MinimumBsmpVersion;
            contents.Manifest.ReleaseNotes = contents.ReleaseNotes;
            contents.Manifest.FileHashes = contents.Manifest.Components.ToDictionary(c => c.Path, c => c.Sha256, StringComparer.Ordinal);
            contents.Plugins = ReadPluginDescriptors(entries, byType);
            ValidateTypedContents(contents);
            return contents;
        }

        static ConfigPackageContents ReadLegacy(string packagePath, Dictionary<string, ZipArchiveEntry> entries, JObject manifestObject)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "PackageId", "Version", "CreatedByOperator", "CreatedAtUtc", "MissionPlannerVersion", "FileHashes", "ReleaseNotes"
            };
            RejectUnknownProperties(manifestObject, allowed, ManifestEntryName);
            var fileHashes = manifestObject["FileHashes"]?.ToObject<Dictionary<string, string>>()
                ?? throw new InvalidDataException("Legacy manifest has no FileHashes object.");
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntryName };
            foreach (var kv in fileHashes)
            {
                ValidateEntryPath(kv.Key);
                if (!IsLowerHexHash(kv.Value)) throw new InvalidDataException("Legacy manifest has an invalid SHA-256 for '" + kv.Key + "'.");
                declared.Add(kv.Key);
                if (!entries.TryGetValue(kv.Key, out var entry)) throw new InvalidDataException("Legacy package is missing '" + kv.Key + "'.");
                var actual = BsaHash.ComputeSha256Hex(ReadEntryText(entry));
                if (!string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Legacy package entry '" + kv.Key + "' failed its integrity check.");
            }
            foreach (var path in entries.Keys)
                if (!declared.Contains(path)) throw new InvalidDataException("Legacy package contains undeclared entry '" + path + "'.");
            if (!entries.TryGetValue(ConfigSubsetEntryName, out var subsetEntry))
                throw new InvalidDataException("Legacy package is missing " + ConfigSubsetEntryName + ".");

            var manifest = new PackageManifest
            {
                PackageId = (string)manifestObject["PackageId"],
                Version = (string)manifestObject["Version"],
                CreatedByOperator = (string)manifestObject["CreatedByOperator"],
                CreatedAtUtc = manifestObject["CreatedAtUtc"]?.ToObject<DateTime>() ?? DateTime.MinValue,
                MissionPlannerVersion = (string)manifestObject["MissionPlannerVersion"],
                ReleaseNotes = (string)manifestObject["ReleaseNotes"],
                FileHashes = fileHashes
            };
            return new ConfigPackageContents
            {
                Manifest = manifest,
                ConfigSubset = ParseObject(ReadEntryBytes(subsetEntry), ConfigSubsetEntryName).ToObject<Dictionary<string, string>>()
                    ?? new Dictionary<string, string>(),
                ChecklistJson = ReadEntryTextOrNull(entries, ChecklistEntryName) ?? ReadEntryTextOrNull(entries, LegacyChecklistEntryName),
                KeyPolicyJson = ReadEntryTextOrNull(entries, KeyPolicyEntryName),
                LockPolicyJson = ReadEntryTextOrNull(entries, LockPolicyEntryName),
                ReleaseNotes = manifest.ReleaseNotes,
                IsLegacy = true
            };
        }

        static Dictionary<string, ZipArchiveEntry> ValidateArchiveEnvelope(ZipArchive archive)
        {
            if (archive.Entries.Count > MaximumEntryCount)
                throw new InvalidDataException("Package exceeds the " + MaximumEntryCount + " entry limit.");
            var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                ValidateEntryPath(entry.FullName);
                if (result.ContainsKey(entry.FullName))
                    throw new InvalidDataException("Package contains duplicate path '" + entry.FullName + "'.");
                result.Add(entry.FullName, entry);
                if (entry.Length > MaximumEntryBytes)
                    throw new InvalidDataException("Entry '" + entry.FullName + "' exceeds the per-entry size limit.");
                total = checked(total + entry.Length);
                if (total > MaximumExpandedBytes) throw new InvalidDataException("Package exceeds the expanded-size limit.");
                if (entry.Length > 0 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > MaximumCompressionRatio))
                    throw new InvalidDataException("Entry '" + entry.FullName + "' exceeds the compression-ratio limit.");
                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType == 0xA000) throw new InvalidDataException("Symbolic links are not permitted in bundles.");
                RejectSourcePayload(entry.FullName);
            }
            return result;
        }

        static void ValidateV2Manifest(PackageManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported or missing bundle SchemaVersion.");
            if (string.IsNullOrWhiteSpace(manifest.PackageId) || !IsReverseDnsId(manifest.PackageId))
                throw new InvalidDataException("PackageId must be a stable reverse-DNS identifier.");
            ValidateSemVer(manifest.PackageVersion, "PackageVersion");
            if (string.IsNullOrWhiteSpace(manifest.CreatedByOperator)) throw new InvalidDataException("CreatedByOperator is required.");
            if (manifest.CreatedAtUtc == default(DateTime)) throw new InvalidDataException("CreatedAtUtc is required.");
            if (manifest.Compatibility == null) throw new InvalidDataException("Compatibility is required.");
            ValidateVersion(manifest.Compatibility.MinimumBsmpVersion, "Compatibility.MinimumBsmpVersion");
            if (!string.IsNullOrWhiteSpace(manifest.Compatibility.MaximumBsmpVersionExclusive))
                ValidateVersion(manifest.Compatibility.MaximumBsmpVersionExclusive, "Compatibility.MaximumBsmpVersionExclusive");
            if (manifest.Components == null || manifest.Components.Count == 0) throw new InvalidDataException("Components must not be empty.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var component in manifest.Components)
            {
                if (component == null || string.IsNullOrWhiteSpace(component.ComponentId) || !ids.Add(component.ComponentId))
                    throw new InvalidDataException("Component IDs must be present and unique.");
                if (!KnownTypes.Contains(component.Type)) throw new InvalidDataException("Unknown component type '" + component.Type + "'.");
                ValidateEntryPath(component.Path);
                if (FixedPathsByType.TryGetValue(component.Type, out var fixedPath) && component.Path != fixedPath)
                    throw new InvalidDataException("Component type '" + component.Type + "' must use path '" + fixedPath + "'.");
                if ((component.Type == "plugin-descriptor" || component.Type == "plugin-payload") &&
                    !component.Path.StartsWith("plugins/", StringComparison.Ordinal))
                    throw new InvalidDataException("Plugin components must be contained under plugins/.");
                if (!paths.Add(component.Path)) throw new InvalidDataException("Duplicate component path '" + component.Path + "'.");
                if (!KnownApplyModes.Contains(component.ApplyMode)) throw new InvalidDataException("Unknown apply mode '" + component.ApplyMode + "'.");
                if (component.ByteLength < 0 || component.ByteLength > MaximumEntryBytes) throw new InvalidDataException("Invalid byte length for '" + component.ComponentId + "'.");
                if (!IsLowerHexHash(component.Sha256)) throw new InvalidDataException("Invalid SHA-256 for '" + component.ComponentId + "'.");
                foreach (var capability in component.Capabilities ?? new List<string>())
                    if (!KnownCapabilities.Contains(capability)) throw new InvalidDataException("Unknown capability '" + capability + "'.");
            }
            foreach (var component in manifest.Components)
                foreach (var dependency in component.Dependencies ?? new List<string>())
                    if (!ids.Contains(dependency)) throw new InvalidDataException("Component '" + component.ComponentId + "' has unknown dependency '" + dependency + "'.");
            if (manifest.Components.Count(c => c.Type == "mpconfig-subset") != 1)
                throw new InvalidDataException("A schema-v2 bundle must declare exactly one mpconfig-subset component.");
            if (manifest.Components.Any(c => c.Type == "mpconfig-subset" && !c.Required))
                throw new InvalidDataException("The mpconfig-subset component must be required.");

            var coreCount = manifest.Components.Count(c => c.Type == "quickview-layout" || c.Type == "telemetry-bindings" || c.Type == "warning-profile" || c.Type == "health-rules");
            if (coreCount != 0 && coreCount != 4) throw new InvalidDataException("The core profile is all-or-nothing.");
            if (manifest.Components.Any(c =>
                    (c.Type == "quickview-layout" || c.Type == "telemetry-bindings" || c.Type == "warning-profile" || c.Type == "health-rules") && !c.Required))
                throw new InvalidDataException("Every core-profile component must be required.");
            if (manifest.Components.Any(c => c.Type == "plugin-payload") && (manifest.Signatures == null || manifest.Signatures.Count == 0))
                throw new InvalidDataException("Executable components require a detached BSA signature.");
            foreach (var signature in manifest.Signatures ?? new List<PackageSignature>())
            {
                if (signature.Algorithm != "RSA-SHA256" || string.IsNullOrWhiteSpace(signature.KeyId))
                    throw new InvalidDataException("Only identified RSA-SHA256 detached signatures are supported.");
                ValidateEntryPath(signature.Path);
                if (signature.ByteLength <= 0 || signature.ByteLength > 8192 || !IsLowerHexHash(signature.Sha256))
                    throw new InvalidDataException("Detached signature metadata is invalid.");
            }
        }

        static void ValidateTypedContents(ConfigPackageContents contents)
        {
            if (!contents.HasCompleteCoreProfile &&
                (contents.QuickView != null || contents.TelemetryBindings != null || contents.Warnings != null || contents.HealthRules != null))
                throw new InvalidDataException("The core profile is incomplete.");
            if (!contents.HasCompleteCoreProfile) return;
            try { BsaQuickViewCodec.Validate(contents.QuickView); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { throw new InvalidDataException("QuickView profile is invalid: " + ex.Message, ex); }
            if (contents.TelemetryBindings.Bindings == null ||
                contents.TelemetryBindings.Bindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.FieldId) || b.FieldId.StartsWith("customfield", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Telemetry bindings require stable field IDs.");
            if (contents.Warnings.Rules == null ||
                contents.Warnings.Rules.Any(r => r == null || string.IsNullOrWhiteSpace(r.RuleId) || string.IsNullOrWhiteSpace(r.Text) ||
                    r.Condition == null || string.IsNullOrWhiteSpace(r.Condition.FieldId) ||
                    r.Condition.FieldId.StartsWith("customfield", StringComparison.OrdinalIgnoreCase)) ||
                contents.Warnings.Rules.Select(r => r.RuleId).Distinct(StringComparer.Ordinal).Count() != contents.Warnings.Rules.Count)
                throw new InvalidDataException("Warning rule IDs must be present and unique.");
            if (contents.HealthRules.EvaluationHz <= 0 || contents.HealthRules.EvaluationHz > 4.0)
                throw new InvalidDataException("Health-rule evaluation must be greater than zero and no faster than 4 Hz.");
            if (!string.Equals(contents.Warnings.ApplyMode, "merge", StringComparison.Ordinal) &&
                !string.Equals(contents.Warnings.ApplyMode, "replace-owned", StringComparison.Ordinal))
                throw new InvalidDataException("Warning profile apply mode must be merge or replace-owned.");
            MissionPlanner.BSA.Telemetry.JudicarHealthEvaluator.Validate(contents.HealthRules);
        }

        static IReadOnlyList<BsaPluginDescriptor> ReadPluginDescriptors(Dictionary<string, ZipArchiveEntry> entries,
            Dictionary<string, List<PackageComponent>> byType)
        {
            if (!byType.TryGetValue("plugin-descriptor", out var descriptors)) return new List<BsaPluginDescriptor>();
            return descriptors.Select(c => DeserializeStrict<BsaPluginDescriptor>(ParseObject(ReadEntryBytes(entries[c.Path]), c.Path), c.Path)).ToList();
        }

        static T ReadJsonComponent<T>(Dictionary<string, ZipArchiveEntry> entries,
            Dictionary<string, List<PackageComponent>> byType, string type) where T : class
        {
            if (!byType.TryGetValue(type, out var components)) return null;
            if (components.Count != 1) throw new InvalidDataException("Component type '" + type + "' must occur once.");
            var component = components[0];
            return DeserializeStrict<T>(ParseObject(ReadEntryBytes(entries[component.Path]), component.Path), component.Path);
        }

        static string ReadTextComponent(Dictionary<string, ZipArchiveEntry> entries,
            Dictionary<string, List<PackageComponent>> byType, string type)
        {
            if (!byType.TryGetValue(type, out var components)) return null;
            if (components.Count != 1) throw new InvalidDataException("Component type '" + type + "' must occur once.");
            return StrictUtf8.GetString(ReadEntryBytes(entries[components[0].Path]));
        }

        static JObject ParseObject(byte[] bytes, string path)
        {
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("'" + path + "' is not valid UTF-8.", ex); }
            try
            {
                return JObject.Parse(text, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Load
                });
            }
            catch (JsonException ex) { throw new InvalidDataException("'" + path + "' is not valid strict JSON: " + ex.Message, ex); }
        }

        static T DeserializeStrict<T>(JObject value, string path)
        {
            try
            {
                return value.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    DateParseHandling = DateParseHandling.DateTime,
                    Culture = CultureInfo.InvariantCulture
                }));
            }
            catch (JsonException ex) { throw new InvalidDataException("'" + path + "' has an invalid schema: " + ex.Message, ex); }
        }

        static void RejectUnknownProperties(JObject value, HashSet<string> allowed, string path)
        {
            foreach (var property in value.Properties())
                if (!allowed.Contains(property.Name)) throw new InvalidDataException("'" + path + "' contains unknown property '" + property.Name + "'.");
        }

        static void ValidateEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") || path.EndsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException("Unsafe bundle path '" + path + "'.");
            var parts = path.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new InvalidDataException("Unsafe bundle path '" + path + "'.");
        }

        static void RejectSourcePayload(string path)
        {
            var extension = Path.GetExtension(path);
            if (new[] { ".cs", ".csx", ".csproj", ".sln", ".vb", ".vbproj", ".fs", ".fsproj", ".ps1", ".cmd", ".bat", ".js", ".jsx", ".ts", ".tsx", ".vbs", ".py", ".sh", ".java", ".cpp", ".c", ".h", ".rs", ".go" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Source code and scripts are not permitted in bundles: '" + path + "'.");
        }

        static bool IsLowerHexHash(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        static bool IsReverseDnsId(string value)
        {
            var parts = value.Split('.');
            return parts.Length >= 3 && parts.All(p => p.Length > 0 && char.IsLetterOrDigit(p[0]) && p.All(c => char.IsLetterOrDigit(c) || c == '-'));
        }

        static void ValidateSemVer(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(field + " is required.");
            var core = value.Split('-')[0].Split('+')[0].Split('.');
            if (core.Length != 3 || core.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException(field + " must be SemVer (major.minor.patch).");
        }

        static void ValidateVersion(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || !Version.TryParse(value, out _))
                throw new InvalidDataException(field + " must be a valid BSMP version.");
        }

        static byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using (var input = entry.Open())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                long total = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaximumEntryBytes || total > entry.Length + 1)
                        throw new InvalidDataException("Entry '" + entry.FullName + "' exceeded its declared or permitted size while reading.");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        static string ReadEntryText(ZipArchiveEntry entry) => StrictUtf8.GetString(ReadEntryBytes(entry));
        static string ReadEntryTextOrNull(Dictionary<string, ZipArchiveEntry> entries, string name) =>
            entries.TryGetValue(name, out var entry) ? ReadEntryText(entry) : null;

        static void WriteEntry(ZipArchive archive, string entryName, byte[] content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open()) stream.Write(content, 0, content.Length);
        }

        static void AddJson(List<PendingEntry> entries, string id, string type, string path, bool required,
            string applyMode, object value, bool restartRequired) =>
            AddBytes(entries, id, type, path, required, applyMode,
                StrictUtf8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented)), restartRequired);

        static void AddText(List<PendingEntry> entries, string id, string type, string path, bool required,
            string applyMode, string value, bool restartRequired) =>
            AddBytes(entries, id, type, path, required, applyMode, StrictUtf8.GetBytes(value ?? string.Empty), restartRequired);

        static void AddTextFile(List<PendingEntry> entries, string id, string type, string path, bool required,
            string applyMode, string sourcePath, string label)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Could not find the " + label + " file to include in the package: " + sourcePath);
            AddText(entries, id, type, path, required, applyMode, File.ReadAllText(sourcePath), true);
        }

        static void AddBytes(List<PendingEntry> entries, string id, string type, string path, bool required,
            string applyMode, byte[] bytes, bool restartRequired) => entries.Add(new PendingEntry
        {
            Bytes = bytes,
            Component = new PackageComponent
            {
                ComponentId = id,
                Type = type,
                Path = path,
                Required = required,
                ApplyMode = applyMode,
                RestartRequired = restartRequired
            }
        });

        static SortedDictionary<string, string> SortedCopy(IReadOnlyDictionary<string, string> source) =>
            new SortedDictionary<string, string>(source.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal);

        sealed class PendingEntry
        {
            public PackageComponent Component { get; set; }
            public byte[] Bytes { get; set; }
        }
    }
}
