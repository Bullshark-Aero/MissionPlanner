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

        /// <summary>Null for an ungrouped checklist - see PreflightCheckDefinition.Group.</summary>
        public string Group { get; set; }
    }

    /// <summary>One Auto check whose latest re-verified value (at AwaitingSignOff entry or the Sign
    /// Off click) differs from what was first shown when its page was displayed - durable evidence
    /// that autos were re-checked at the decision point and what moved, independent of whether that
    /// difference ever blocked a Sign Off click. See PreflightRun.HasAutoReverifyChange.</summary>
    public class AutoReverifyChangeReportEntry
    {
        public string CheckId { get; set; }
        public string Title { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public string Detail { get; set; }
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

        /// <summary>Every Auto check whose latest value moved from what its page first showed - see
        /// AutoReverifyChangeReportEntry. Distinct from ChangedAnswerCheckIds, which is
        /// operator-authored changes only (PreflightRun.HasChangedAnswer).</summary>
        public List<AutoReverifyChangeReportEntry> AutoReverifyChanges { get; set; } = new List<AutoReverifyChangeReportEntry>();

        /// <summary>Titles of checks whose final answer is Fail/Unknown at Critical severity (drove a
        /// NoGo) or Warning severity (drove a Warning) - a scannable summary on top of FinalAnswers.</summary>
        public List<string> CriticalIssues { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
