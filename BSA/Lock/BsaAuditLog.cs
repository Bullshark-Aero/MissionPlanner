using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>One audit record - every BsaLockService check while the lock is On writes one of
    /// these, regardless of class (per WP3: "all four classes are audit-logged").</summary>
    public class AuditEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string ActionId { get; set; }
        public string MatchValue { get; set; }
        public string Class { get; set; }
        public string Reason { get; set; }
        public string Outcome { get; set; }
    }

    /// <summary>
    /// Append-only JSONL audit trail in BSA\audit\{yyyyMMdd}.jsonl - day-rotated, unlike WP1's
    /// one-file-per-run reports, since this is a continuous log of individual actions rather than a
    /// discrete per-run artifact. Plain static file I/O, same convention as
    /// BSA.Reports.PreflightReportWriter.
    /// </summary>
    public static class BsaAuditLog
    {
        public static void Append(string directory, AuditEntry entry)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory is required.", nameof(directory));
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            Directory.CreateDirectory(directory);
            var path = FilePathForDay(directory, entry.TimestampUtc);
            var line = JsonConvert.SerializeObject(entry, Formatting.None);
            File.AppendAllText(path, line + Environment.NewLine);
        }

        public static List<AuditEntry> ReadDay(string directory, DateTime dayUtc)
        {
            var path = FilePathForDay(directory, dayUtc);
            if (!File.Exists(path))
                return new List<AuditEntry>();

            return File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonConvert.DeserializeObject<AuditEntry>(line))
                .ToList();
        }

        static string FilePathForDay(string directory, DateTime dayUtc) =>
            Path.Combine(directory, dayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");
    }
}
