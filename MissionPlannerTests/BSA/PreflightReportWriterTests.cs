using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Reports;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class PreflightReportWriterTests
    {
        static PreflightRun BuildSampleRun()
        {
            var run = new PreflightRun(new List<PreflightCheckDefinition>
                {
                    new PreflightCheckDefinition
                    {
                        Id = WellKnownCheckIds.CorrectAircraft, Title = "Correct aircraft",
                        Type = CheckType.Manual, Severity = CheckSeverity.Critical
                    },
                    new PreflightCheckDefinition
                    {
                        Id = "prop-clear", Title = "Prop area clear",
                        Type = CheckType.Manual, Severity = CheckSeverity.Critical
                    }
                })
            {
                StartedUtc = new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc),
                EndedUtc = new DateTime(2026, 7, 9, 8, 5, 0, DateTimeKind.Utc),
                State = PreflightRunState.Completed,
                Result = PreflightResult.NoGo,
                OperatorName = "Jane Pilot"
            };

            run.History.Add(new CheckResultRecord
            {
                CheckId = WellKnownCheckIds.CorrectAircraft,
                CheckTitle = "Correct aircraft",
                Severity = CheckSeverity.Critical,
                Outcome = CheckOutcome.Pass,
                Notes = "BSA-001",
                TimestampUtc = run.StartedUtc
            });
            run.History.Add(new CheckResultRecord
            {
                CheckId = "prop-clear",
                CheckTitle = "Prop area clear",
                Severity = CheckSeverity.Critical,
                Outcome = CheckOutcome.Fail,
                Notes = "tool left near prop",
                TimestampUtc = run.StartedUtc
            });

            return run;
        }

        static PreflightRun BuildGroupedRunWithAutoReverifyChange()
        {
            var run = new PreflightRun(new List<PreflightCheckDefinition>
                {
                    new PreflightCheckDefinition
                    {
                        Id = "correct-aircraft", Title = "Correct aircraft",
                        Type = CheckType.Manual, Severity = CheckSeverity.Critical, Group = "Walkaround"
                    },
                    new PreflightCheckDefinition
                    {
                        Id = "mission-unchanged", Title = "Mission unchanged",
                        Type = CheckType.Auto, Severity = CheckSeverity.Critical, Group = "System checks"
                    }
                })
            {
                StartedUtc = new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc),
                EndedUtc = new DateTime(2026, 7, 9, 8, 5, 0, DateTimeKind.Utc),
                State = PreflightRunState.Completed,
                Result = PreflightResult.NoGo,
                OperatorName = "Jane Pilot"
            };

            run.History.Add(new CheckResultRecord
            {
                CheckId = "correct-aircraft", CheckTitle = "Correct aircraft", Severity = CheckSeverity.Critical,
                Group = "Walkaround", Outcome = CheckOutcome.Pass, Notes = "BSA-001", TimestampUtc = run.StartedUtc,
                Source = CheckResultSource.Operator
            });
            run.History.Add(new CheckResultRecord
            {
                CheckId = "mission-unchanged", CheckTitle = "Mission unchanged", Severity = CheckSeverity.Critical,
                Group = "System checks", Outcome = CheckOutcome.Pass, Detail = "hash match", TimestampUtc = run.StartedUtc,
                Source = CheckResultSource.AutoInitial
            });
            run.History.Add(new CheckResultRecord
            {
                CheckId = "mission-unchanged", CheckTitle = "Mission unchanged", Severity = CheckSeverity.Critical,
                Group = "System checks", Outcome = CheckOutcome.Fail, Detail = "mission changed",
                TimestampUtc = run.EndedUtc.Value, Source = CheckResultSource.AutoReverify
            });

            return run;
        }

        [TestMethod]
        public void BuildReport_PopulatesGroupOnEntries()
        {
            var report = PreflightReportWriter.BuildReport(BuildGroupedRunWithAutoReverifyChange(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            var entry = report.FinalAnswers.Find(e => e.CheckId == "correct-aircraft");
            Assert.AreEqual("Walkaround", entry.Group);
        }

        [TestMethod]
        public void BuildReport_PopulatesAutoReverifyChanges_WhenLatestDiffersFromInitial()
        {
            var report = PreflightReportWriter.BuildReport(BuildGroupedRunWithAutoReverifyChange(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");

            Assert.AreEqual(1, report.AutoReverifyChanges.Count);
            var change = report.AutoReverifyChanges[0];
            Assert.AreEqual("mission-unchanged", change.CheckId);
            Assert.AreEqual("Pass", change.Before);
            Assert.AreEqual("Fail", change.After);
        }

        [TestMethod]
        public void BuildReport_AutoReverifyChanges_EmptyWhenNothingMoved()
        {
            var report = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            Assert.AreEqual(0, report.AutoReverifyChanges.Count);
        }

        [TestMethod]
        public void Write_Html_RendersGroupSectionHeadersAndAutoReverifySection()
        {
            var report = PreflightReportWriter.BuildReport(BuildGroupedRunWithAutoReverifyChange(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            var dir = Path.Combine(Path.GetTempPath(), "BsaPreflightReportWriterTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var (_, htmlPath) = PreflightReportWriter.Write(report, dir);
                var html = File.ReadAllText(htmlPath);

                StringAssert.Contains(html, "<h3>Walkaround</h3>");
                StringAssert.Contains(html, "<h3>System checks</h3>");
                StringAssert.Contains(html, "Automatic checks re-verified at sign-off");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void BuildReport_PopulatesAircraftIdFromCorrectAircraftCheckNote()
        {
            var report = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            Assert.AreEqual("BSA-001", report.AircraftIdNote);
        }

        [TestMethod]
        public void BuildReport_CollectsCriticalIssues()
        {
            var report = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            CollectionAssert.Contains(report.CriticalIssues, "Prop area clear");
        }

        [TestMethod]
        public void BuildReport_FinalAnswers_OneEntryPerCheck()
        {
            var report = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            Assert.AreEqual(2, report.FinalAnswers.Count);
        }

        [TestMethod]
        public void Write_ProducesJsonAndHtml_ThatBothExist()
        {
            var report = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
            var dir = Path.Combine(Path.GetTempPath(), "BsaPreflightReportWriterTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var (jsonPath, htmlPath) = PreflightReportWriter.Write(report, dir);
                Assert.IsTrue(File.Exists(jsonPath));
                Assert.IsTrue(File.Exists(htmlPath));
                StringAssert.Contains(File.ReadAllText(jsonPath), report.RunId);
                StringAssert.Contains(File.ReadAllText(htmlPath), "NoGo");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Write_NeverOverwrites_SecondRunGetsDifferentFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "BsaPreflightReportWriterTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var report1 = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");
                var report2 = PreflightReportWriter.BuildReport(BuildSampleRun(), "1.3.80", "hash1", "pending-wp2", 1, "QuadPlane");

                var (json1, _) = PreflightReportWriter.Write(report1, dir);
                var (json2, _) = PreflightReportWriter.Write(report2, dir);

                Assert.AreNotEqual(json1, json2); // different RunId per PreflightRun -> different filename
                Assert.IsTrue(File.Exists(json1));
                Assert.IsTrue(File.Exists(json2));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void SanitizeForFilename_RemovesInvalidCharsAndSpaces()
        {
            var result = PreflightReportWriter.SanitizeForFilename("BSA/001: \"Test\" <aircraft>");
            foreach (var c in Path.GetInvalidFileNameChars())
                Assert.IsFalse(result.Contains(c.ToString()));
            Assert.IsFalse(result.Contains(" "));
        }

        [TestMethod]
        public void SanitizeForFilename_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, PreflightReportWriter.SanitizeForFilename(null));
            Assert.AreEqual(string.Empty, PreflightReportWriter.SanitizeForFilename("   "));
        }

        [TestMethod]
        public void EscapeHtml_EscapesAngleBrackets()
        {
            var result = PreflightReportWriter.EscapeHtml("<script>alert(1)</script>");
            Assert.IsFalse(result.Contains("<script>"));
        }
    }
}
