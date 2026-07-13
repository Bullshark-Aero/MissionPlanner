using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Config
{
    /// <summary>Everything read back out of a .bsampconfig package by Read().</summary>
    public class ConfigPackageContents
    {
        public PackageManifest Manifest { get; set; }
        public IReadOnlyDictionary<string, string> ConfigSubset { get; set; }
        public bool HasLockPolicy { get; set; }
    }

    /// <summary>
    /// Reads and writes the .bsampconfig package format: a ZIP archive (System.IO.Compression, already
    /// referenced by this project - see ExtLibs/ArduPilot/Joystick/JoystickBase.cs for the existing
    /// precedent) containing manifest.json, the curated config subset, copies of the BSA config files
    /// that produced this package, and release notes. All entries here are UTF-8 text (JSON or
    /// markdown) - hashed and compared as text via BsaHash, not raw bytes, keeping this consistent with
    /// BsaHash's existing canonical-JSON convention.
    /// </summary>
    public static class BsaConfigPackage
    {
        public const string ManifestEntryName = "manifest.json";
        public const string ConfigSubsetEntryName = "mpconfig/config.subset.json";
        public const string ChecklistEntryName = "bsa/preflight_checks.default.json";
        public const string KeyPolicyEntryName = "bsa/bsa_key_policy.json";
        public const string LockPolicyEntryName = "bsa/lock_policy.json";
        public const string ReleaseNotesEntryName = "RELEASE_NOTES.md";

        /// <returns>The manifest that was written, including the computed per-entry hashes.</returns>
        public static PackageManifest Write(string outputPath, IReadOnlyDictionary<string, string> subsetConfig,
            string checklistJsonPath, string keyPolicyJsonPath, string lockPolicyJsonPathOrNull,
            string version, string createdByOperator, string missionPlannerVersion, string releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("outputPath is required.", nameof(outputPath));
            if (subsetConfig == null)
                throw new ArgumentNullException(nameof(subsetConfig));

            var entries = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConfigSubsetEntryName] = JsonConvert.SerializeObject(SortedCopy(subsetConfig), Formatting.Indented),
                [ChecklistEntryName] = ReadTextOrThrow(checklistJsonPath, "checklist"),
                [KeyPolicyEntryName] = ReadTextOrThrow(keyPolicyJsonPath, "key policy"),
                [ReleaseNotesEntryName] = releaseNotes ?? string.Empty
            };

            if (!string.IsNullOrEmpty(lockPolicyJsonPathOrNull) && File.Exists(lockPolicyJsonPathOrNull))
                entries[LockPolicyEntryName] = File.ReadAllText(lockPolicyJsonPathOrNull);

            var manifest = new PackageManifest
            {
                Version = version,
                CreatedByOperator = createdByOperator,
                CreatedAtUtc = DateTime.UtcNow,
                MissionPlannerVersion = missionPlannerVersion,
                ReleaseNotes = releaseNotes
            };
            foreach (var kv in entries)
                manifest.FileHashes[kv.Key] = BsaHash.ComputeSha256Hex(kv.Value);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, ManifestEntryName, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                foreach (var kv in entries)
                    WriteEntry(archive, kv.Key, kv.Value);
            }

            return manifest;
        }

        /// <summary>
        /// Verifies every entry the manifest claims to have (hash must match - a tampered or corrupted
        /// package throws rather than silently returning partial/wrong data, matching this codebase's
        /// existing fail-closed config-loading convention) before returning the package contents.
        /// </summary>
        public static ConfigPackageContents Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"Package not found: {path}");

            using (var archive = ZipFile.OpenRead(path))
            {
                var manifestEntry = archive.GetEntry(ManifestEntryName)
                    ?? throw new InvalidDataException($"'{path}' is not a valid .bsampconfig package (missing {ManifestEntryName}).");
                var manifest = JsonConvert.DeserializeObject<PackageManifest>(ReadEntryText(manifestEntry))
                    ?? throw new InvalidDataException($"'{path}' has an unreadable manifest.");

                foreach (var kv in manifest.FileHashes)
                {
                    var entry = archive.GetEntry(kv.Key)
                        ?? throw new InvalidDataException($"'{path}' is missing an entry listed in its manifest: {kv.Key}.");
                    var actualHash = BsaHash.ComputeSha256Hex(ReadEntryText(entry));
                    if (!string.Equals(actualHash, kv.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"'{path}' failed integrity check: '{kv.Key}' does not match its recorded hash (tampered or corrupted).");
                }

                var subsetEntry = archive.GetEntry(ConfigSubsetEntryName)
                    ?? throw new InvalidDataException($"'{path}' is not a valid .bsampconfig package (missing {ConfigSubsetEntryName}).");
                var subset = JsonConvert.DeserializeObject<Dictionary<string, string>>(ReadEntryText(subsetEntry))
                    ?? new Dictionary<string, string>();

                return new ConfigPackageContents
                {
                    Manifest = manifest,
                    ConfigSubset = subset,
                    HasLockPolicy = archive.GetEntry(LockPolicyEntryName) != null
                };
            }
        }

        static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var entryStream = entry.Open())
            using (var writer = new StreamWriter(entryStream, new UTF8Encoding(false)))
                writer.Write(content ?? string.Empty);
        }

        static string ReadEntryText(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        static string ReadTextOrThrow(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"Could not find the {label} file to include in the package: {path}");
            return File.ReadAllText(path);
        }

        static SortedDictionary<string, string> SortedCopy(IReadOnlyDictionary<string, string> source) =>
            new SortedDictionary<string, string>(source.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal);
    }
}
