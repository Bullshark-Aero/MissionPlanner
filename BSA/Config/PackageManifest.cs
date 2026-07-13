using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// manifest.json inside a .bsampconfig package. FileHashes maps each other zip-entry name to the
    /// SHA-256 (via BsaHash) of its UTF-8 text content at write time, so Read() can detect a tampered
    /// or corrupted entry before anything in the package is trusted.
    /// </summary>
    public class PackageManifest
    {
        public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
        public string Version { get; set; }
        public string CreatedByOperator { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string MissionPlannerVersion { get; set; }
        public Dictionary<string, string> FileHashes { get; set; } = new Dictionary<string, string>();
        public string ReleaseNotes { get; set; }
    }
}
