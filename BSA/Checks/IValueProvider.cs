namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Abstracts "read the current value of field X" for one CheckSource (Telemetry/Param/MpConfig) so
    /// AutoCheckEvaluator is testable without MAVLink or WinForms. Concrete implementations
    /// (TelemetryValueProvider, ParamValueProvider, SettingsValueProvider) are constructor-injected with
    /// their underlying source rather than reaching for a static/global - unlike
    /// Controls.PreFlight.CheckListItem's mutable static defaultsrc.
    /// </summary>
    public interface IValueProvider
    {
        /// <summary>
        /// Returns false if the field is unknown or the value is currently unavailable (not downloaded,
        /// disconnected, etc.) - never throws. Callers map a false result to CheckOutcome.Unknown.
        /// </summary>
        bool TryGetValue(string field, out object value);
    }
}
