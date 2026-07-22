using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CheckType
    {
        Manual,
        Auto,
        Semi
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CheckSeverity
    {
        Critical,
        Warning,
        Info
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CheckSource
    {
        Telemetry,
        Param,
        MpConfig,
        Mission,
        Surface
    }

    /// <summary>
    /// Matches Controls.PreFlight.CheckListItem.Conditional semantics exactly.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CheckCondition
    {
        LT,
        LTEQ,
        EQ,
        GT,
        GTEQ,
        NEQ
    }

    /// <summary>
    /// One row of preflight_checks.default.json. Auto/Semi checks are expressed one of two ways:
    /// generic field-comparator (Field+Condition+Value, against Source) or named registered check
    /// (Check, a key into RegisteredCheckRegistry) - never both. See PreflightChecklistLoader for
    /// the validation that enforces this.
    /// </summary>
    public class PreflightCheckDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public CheckType? Type { get; set; }
        public CheckSeverity? Severity { get; set; }

        public CheckSource? Source { get; set; }

        // Generic field-comparator shape.
        public string Field { get; set; }
        public CheckCondition? Condition { get; set; }
        public object Value { get; set; }

        // Named registered-check shape.
        public string Check { get; set; }

        // Manual/Semi: shown to the operator.
        public string Instruction { get; set; }

        public bool RequiresNoteOnFail { get; set; }
        public bool AllowNotApplicable { get; set; } = true;

        /// <summary>Display group name. Must match one of Metadata.Groups exactly (loader-enforced)
        /// when any group is declared; must be left unset entirely when none is. See
        /// PreflightPagePlan. Ignored for placement if this check is hoisted to the auto page (see
        /// AutoChecksFirst) - Group still records where it "belongs" for the sign-off summary.</summary>
        public string Group { get; set; }

        /// <summary>Auto/Semi only. True marks a check whose real answer only exists at sign-off
        /// (e.g. mission-unchanged-during-preflight, whose comparison is a tautology moments after
        /// the run starts). Such a check renders on its page as "verified at sign-off" instead of a
        /// premature outcome, records Unknown with that explanation initially (never blocks page
        /// advance), and is re-evaluated for real when the operator reaches sign-off, alongside every
        /// other Auto check. See WP1_wizard_grouping_pagination_plan.md §4b.</summary>
        public bool DeferredToSignOff { get; set; }
    }
}
