using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Reports
{
    /// <summary>
    /// Builds a PreflightReport from a finished (Completed or Aborted) PreflightRun and writes it as
    /// JSON (authoritative) + HTML (a rendering of the same object) to disk. Filenames are structurally
    /// unique ({timestamp}_{runId}[_{aircraft}]), so reports are naturally append-only - nothing here
    /// ever opens a file for overwrite. Write() does not swallow I/O failures: the caller (the wizard)
    /// is responsible for treating a failed write as blocking the run from ever being published as GO -
    /// see BsaPreflightService.PublishResult's doc comment.
    /// </summary>
    public static class PreflightReportWriter
    {
        public static PreflightReport BuildReport(PreflightRun run, string missionPlannerVersion,
            string preflightConfigHash, string mpConfigHash, byte? sysid, string frameString)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var latest = run.LatestPerCheck.ToList();
            var finalAnswers = latest.Select(ToEntry).ToList();
            var changedIds = run.Checks.Select(c => c.Id).Where(run.HasChangedAnswer).ToList();

            var aircraftIdNote = latest.FirstOrDefault(r => r.CheckId == WellKnownCheckIds.CorrectAircraft)?.Notes;

            var autoReverifyChanges = run.Checks
                .Where(c => c.Type == CheckType.Auto && run.HasAutoReverifyChange(c.Id))
                .Select(c => BuildAutoReverifyEntry(run, c))
                .ToList();

            return new PreflightReport
            {
                RunId = run.RunId,
                StartedUtc = run.StartedUtc,
                EndedUtc = run.EndedUtc,
                Result = run.Result.ToString(),
                AbortReason = run.AbortReason,

                OperatorName = run.OperatorName,
                AircraftIdNote = aircraftIdNote,
                Sysid = sysid,
                FrameString = frameString,

                MissionPlannerVersion = missionPlannerVersion,
                PreflightConfigHash = preflightConfigHash,
                MpConfigHash = mpConfigHash,

                FinalAnswers = finalAnswers,
                FullHistory = run.History.Select(ToEntry).ToList(),
                ChangedAnswerCheckIds = changedIds,
                AutoReverifyChanges = autoReverifyChanges,

                CriticalIssues = latest.Where(r => r.Severity == CheckSeverity.Critical && IsBlocking(r.Outcome))
                    .Select(r => r.CheckTitle).ToList(),
                Warnings = latest.Where(r => r.Severity == CheckSeverity.Warning && IsBlocking(r.Outcome))
                    .Select(r => r.CheckTitle).ToList()
            };
        }

        static bool IsBlocking(CheckOutcome outcome) => outcome == CheckOutcome.Fail || outcome == CheckOutcome.Unknown;

        static PreflightCheckReportEntry ToEntry(CheckResultRecord r) => new PreflightCheckReportEntry
        {
            CheckId = r.CheckId,
            Title = r.CheckTitle,
            Severity = r.Severity.ToString(),
            Outcome = r.Outcome.ToString(),
            Notes = r.Notes,
            Detail = r.Detail,
            TimestampUtc = r.TimestampUtc,
            Group = r.Group
        };

        static AutoReverifyChangeReportEntry BuildAutoReverifyEntry(PreflightRun run, PreflightCheckDefinition check)
        {
            var initial = run.History.First(r => r.CheckId == check.Id && r.Source == CheckResultSource.AutoInitial);
            var latest = run.LatestPerCheck.First(r => r.CheckId == check.Id);
            return new AutoReverifyChangeReportEntry
            {
                CheckId = check.Id,
                Title = check.Title,
                Before = initial.Outcome.ToString(),
                After = latest.Outcome.ToString(),
                Detail = latest.Detail
            };
        }

        /// <returns>The JSON and HTML file paths written.</returns>
        public static (string jsonPath, string htmlPath) Write(PreflightReport report, string directory)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            Directory.CreateDirectory(directory);

            var baseName = BuildBaseFileName(report);
            var jsonPath = Path.Combine(directory, baseName + ".json");
            var htmlPath = Path.Combine(directory, baseName + ".html");

            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            File.WriteAllText(htmlPath, RenderHtml(report));

            return (jsonPath, htmlPath);
        }

        static string BuildBaseFileName(PreflightReport report)
        {
            var timestamp = report.StartedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var aircraft = SanitizeForFilename(report.AircraftIdNote);
            return string.IsNullOrEmpty(aircraft)
                ? $"{timestamp}_{report.RunId}"
                : $"{timestamp}_{report.RunId}_{aircraft}";
        }

        static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public static string SanitizeForFilename(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in text.Trim())
                sb.Append(InvalidFileNameChars.Contains(c) || c == ' ' ? '_' : c);

            var result = sb.ToString();
            return result.Length > 40 ? result.Substring(0, 40) : result;
        }

        public static string EscapeHtml(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : System.Net.WebUtility.HtmlEncode(text);

        static string RenderHtml(PreflightReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>BSA Preflight Report " +
                          EscapeHtml(report.RunId) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;margin:2em;} table{border-collapse:collapse;width:100%;} " +
                          "th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;vertical-align:top;} " +
                          "th{background:#eee;} .Pass{color:#080;} .Fail{color:#b00;} .Unknown{color:#b80;} " +
                          ".NotApplicable{color:#888;} .changed{background:#ffe;}</style></head><body>");

            sb.AppendLine($"<h1>BSA Preflight Report - {EscapeHtml(report.Result)}</h1>");
            sb.AppendLine("<table>");
            AppendRow(sb, "Run Id", report.RunId);
            AppendRow(sb, "Started (UTC)", report.StartedUtc.ToString("u", CultureInfo.InvariantCulture));
            AppendRow(sb, "Ended (UTC)", report.EndedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "-");
            AppendRow(sb, "Result", report.Result);
            if (!string.IsNullOrEmpty(report.AbortReason))
                AppendRow(sb, "Abort reason", report.AbortReason);
            AppendRow(sb, "Operator", report.OperatorName);
            AppendRow(sb, "Aircraft", report.AircraftIdNote);
            AppendRow(sb, "Sysid", report.Sysid?.ToString(CultureInfo.InvariantCulture) ?? "-");
            AppendRow(sb, "Frame", report.FrameString);
            AppendRow(sb, "Mission Planner version", report.MissionPlannerVersion);
            AppendRow(sb, "Preflight config hash", report.PreflightConfigHash);
            AppendRow(sb, "MP config hash", report.MpConfigHash);
            sb.AppendLine("</table>");

            if (report.CriticalIssues.Count > 0)
                AppendList(sb, "Critical issues", report.CriticalIssues);
            if (report.Warnings.Count > 0)
                AppendList(sb, "Warnings", report.Warnings);

            if (report.AutoReverifyChanges.Count > 0)
            {
                sb.AppendLine("<h2>Automatic checks re-verified at sign-off</h2>" +
                              "<table><tr><th>Check</th><th>Was</th><th>Now</th><th>Detail</th></tr>");
                foreach (var change in report.AutoReverifyChanges)
                    sb.AppendLine("<tr>" +
                                  $"<td>{EscapeHtml(change.Title)}</td>" +
                                  $"<td class=\"{EscapeHtml(change.Before)}\">{EscapeHtml(change.Before)}</td>" +
                                  $"<td class=\"{EscapeHtml(change.After)}\">{EscapeHtml(change.After)}</td>" +
                                  $"<td>{EscapeHtml(change.Detail)}</td></tr>");
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<h2>Checks</h2>");
            // Section per Group, in first-appearance order - an ungrouped checklist (every entry's
            // Group is null) falls through to one flat table with no sub-headings, matching the
            // pre-grouping report layout exactly.
            if (report.FinalAnswers.Any(e => !string.IsNullOrEmpty(e.Group)))
            {
                foreach (var group in report.FinalAnswers.Select(e => e.Group ?? "").Distinct())
                {
                    sb.AppendLine($"<h3>{EscapeHtml(string.IsNullOrEmpty(group) ? "(ungrouped)" : group)}</h3>");
                    AppendChecksTable(sb, report, report.FinalAnswers.Where(e => (e.Group ?? "") == group));
                }
            }
            else
            {
                AppendChecksTable(sb, report, report.FinalAnswers);
            }

            if (report.ChangedAnswerCheckIds.Count > 0)
                sb.AppendLine("<p>* answer was changed during the run - see the full history in the JSON report.</p>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        static void AppendChecksTable(StringBuilder sb, PreflightReport report, IEnumerable<PreflightCheckReportEntry> entries)
        {
            sb.AppendLine("<table><tr><th>Check</th><th>Severity</th><th>Outcome</th>" +
                          "<th>Notes</th><th>Detail</th><th>Time (UTC)</th></tr>");
            foreach (var entry in entries)
            {
                var changed = report.ChangedAnswerCheckIds.Contains(entry.CheckId);
                sb.AppendLine("<tr" + (changed ? " class=\"changed\"" : "") + ">" +
                              $"<td>{EscapeHtml(entry.Title)}{(changed ? " *" : "")}</td>" +
                              $"<td>{EscapeHtml(entry.Severity)}</td>" +
                              $"<td class=\"{EscapeHtml(entry.Outcome)}\">{EscapeHtml(entry.Outcome)}</td>" +
                              $"<td>{EscapeHtml(entry.Notes)}</td>" +
                              $"<td>{EscapeHtml(entry.Detail)}</td>" +
                              $"<td>{entry.TimestampUtc.ToString("u", CultureInfo.InvariantCulture)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        static void AppendRow(StringBuilder sb, string label, string value) =>
            sb.AppendLine($"<tr><th>{EscapeHtml(label)}</th><td>{EscapeHtml(value)}</td></tr>");

        static void AppendList(StringBuilder sb, string heading, List<string> items)
        {
            sb.AppendLine($"<h2>{EscapeHtml(heading)}</h2><ul>");
            foreach (var item in items)
                sb.AppendLine($"<li>{EscapeHtml(item)}</li>");
            sb.AppendLine("</ul>");
        }
    }
}
