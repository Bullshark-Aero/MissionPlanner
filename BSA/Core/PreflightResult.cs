using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Run-level verdict, distinct from CheckOutcome (the per-check answer). A critical Fail and a
    /// critical Unknown both aggregate to NoGo but stay distinguishable at the CheckOutcome level for
    /// audit purposes.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PreflightResult
    {
        Unknown,
        Go,
        NoGo,
        Warning
    }
}
