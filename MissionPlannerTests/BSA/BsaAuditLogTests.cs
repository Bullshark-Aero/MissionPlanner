using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaAuditLogTests
    {
        static string TempDir() => Path.Combine(Path.GetTempPath(), "BsaAuditLogTests_" + Guid.NewGuid().ToString("N"));

        [TestMethod]
        public void Append_CreatesDayFile_OneLinePerEntry()
        {
            var dir = TempDir();
            try
            {
                var day = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
                BsaAuditLog.Append(dir, new AuditEntry { TimestampUtc = day, ActionId = "param_write", MatchValue = "AHRS_ORIENTATION", Class = "Block", Outcome = "Blocked" });
                BsaAuditLog.Append(dir, new AuditEntry { TimestampUtc = day.AddMinutes(1), ActionId = "mission_edit", Class = "Allow", Outcome = "Evaluated" });

                var entries = BsaAuditLog.ReadDay(dir, day);
                Assert.AreEqual(2, entries.Count);
                Assert.AreEqual("param_write", entries[0].ActionId);
                Assert.AreEqual("mission_edit", entries[1].ActionId);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Append_DifferentDays_SeparateFiles()
        {
            var dir = TempDir();
            try
            {
                var day1 = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
                var day2 = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
                BsaAuditLog.Append(dir, new AuditEntry { TimestampUtc = day1, ActionId = "a", Class = "Allow" });
                BsaAuditLog.Append(dir, new AuditEntry { TimestampUtc = day2, ActionId = "b", Class = "Allow" });

                Assert.AreEqual(1, BsaAuditLog.ReadDay(dir, day1).Count);
                Assert.AreEqual(1, BsaAuditLog.ReadDay(dir, day2).Count);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ReadDay_NoFileYet_ReturnsEmpty()
        {
            var dir = TempDir();
            Assert.AreEqual(0, BsaAuditLog.ReadDay(dir, DateTime.UtcNow).Count);
        }

        [TestMethod]
        public void Append_PreservesAllFields()
        {
            var dir = TempDir();
            try
            {
                var day = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
                BsaAuditLog.Append(dir, new AuditEntry
                {
                    TimestampUtc = day,
                    ActionId = "param_write",
                    MatchValue = "ARSPD_AUTOCAL",
                    Class = "Warn",
                    Reason = "field recalibration",
                    Outcome = "Evaluated"
                });

                var entry = BsaAuditLog.ReadDay(dir, day)[0];
                Assert.AreEqual("ARSPD_AUTOCAL", entry.MatchValue);
                Assert.AreEqual("Warn", entry.Class);
                Assert.AreEqual("field recalibration", entry.Reason);
                Assert.AreEqual("Evaluated", entry.Outcome);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
