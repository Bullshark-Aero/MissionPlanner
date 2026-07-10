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
    }
}
