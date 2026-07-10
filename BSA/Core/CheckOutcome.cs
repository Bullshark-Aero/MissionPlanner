using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Per-check answer. "Not yet run" is represented by the absence of a CheckResultRecord for a
    /// check id, not by a value here - see PreflightAggregator, which treats a missing record the
    /// same as Unknown.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CheckOutcome
    {
        Pass,
        Fail,
        NotApplicable,
        Unknown
    }
}
