using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Versioned manifest for a configuration bundle. Schema-v2 components hash raw bytes and declare
    /// their install semantics. Legacy fields are populated only by the v1 adapter.
    /// </summary>
    public class PackageManifest
    {
        public int? SchemaVersion { get; set; }
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }

        [JsonIgnore]
        public string Version
        {
            get => PackageVersion;
            set => PackageVersion = value;
        }

        public string CreatedByOperator { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public PackageCompatibility Compatibility { get; set; }
        public List<PackageComponent> Components { get; set; } = new List<PackageComponent>();
        public List<PackageSignature> Signatures { get; set; } = new List<PackageSignature>();

        [JsonIgnore]
        public string MissionPlannerVersion { get; set; }
        [JsonIgnore]
        public Dictionary<string, string> FileHashes { get; set; } = new Dictionary<string, string>();
        [JsonIgnore]
        public string ReleaseNotes { get; set; }
    }

    public class PackageCompatibility
    {
        public string MinimumBsmpVersion { get; set; }
        public string MaximumBsmpVersionExclusive { get; set; }
    }

    public class PackageComponent
    {
        public string ComponentId { get; set; }
        public string Type { get; set; }
        public string Path { get; set; }
        public bool Required { get; set; }
        public string ApplyMode { get; set; }
        public long ByteLength { get; set; }
        public string Sha256 { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public bool RestartRequired { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
    }

    public class PackageSignature
    {
        public string Algorithm { get; set; }
        public string KeyId { get; set; }
        public string Path { get; set; }
        public long ByteLength { get; set; }
        public string Sha256 { get; set; }
    }
}
