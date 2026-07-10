using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Reports
{
    public class PreflightCheckReportEntry
    {
        public string CheckId { get; set; }
        public string Title { get; set; }
        public string Severity { get; set; }
        public string Outcome { get; set; }
        public string Notes { get; set; }
        public string Detail { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// The one object both the JSON (authoritative) and HTML (a rendering of this object, not a second
    /// source of truth) reports are generated from. Every run - including aborted ones - produces one of
    /// these; PreflightReportWriter never skips a write silently.
    /// </summary>
    public class PreflightReport
    {
        public string RunId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? EndedUtc { get; set; }
        public string Result { get; set; }
        public string AbortReason { get; set; }

        public string OperatorName { get; set; }
        public string AircraftIdNote { get; set; }
        public byte? Sysid { get; set; }
        public string FrameString { get; set; }

        public string MissionPlannerVersion { get; set; }
        public string PreflightConfigHash { get; set; }
        public string MpConfigHash { get; set; }

        /// <summary>Most recent answer per check - what actually determined Result.</summary>
        public List<PreflightCheckReportEntry> FinalAnswers { get; set; } = new List<PreflightCheckReportEntry>();

        /// <summary>Every answer given, in order, including ones later superseded by navigating back
        /// and re-answering - see PreflightRun.HasChangedAnswer.</summary>
        public List<PreflightCheckReportEntry> FullHistory { get; set; } = new List<PreflightCheckReportEntry>();

        public List<string> ChangedAnswerCheckIds { get; set; } = new List<string>();

        /// <summary>Titles of checks whose final answer is Fail/Unknown at Critical severity (drove a
        /// NoGo) or Warning severity (drove a Warning) - a scannable summary on top of FinalAnswers.</summary>
        public List<string> CriticalIssues { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
