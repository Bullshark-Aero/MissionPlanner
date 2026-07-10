namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Check ids referenced from C# code, not just from preflight_checks.default.json. Keeping them as
    /// named constants means renaming a check in JSON without updating the matching C# reference fails
    /// loudly (the id is simply absent from results) rather than silently losing, e.g., the aircraft-id
    /// note from every report.
    /// </summary>
    public static class WellKnownCheckIds
    {
        /// <summary>The manual "correct aircraft connected" check (plan decision #2) - its Notes field
        /// is what PreflightReportWriter uses as the report's aircraft identity.</summary>
        public const string CorrectAircraft = "correct-aircraft";
    }
}
