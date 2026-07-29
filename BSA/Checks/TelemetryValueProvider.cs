using System.Reflection;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Reflects over an injected source object for source: Telemetry checks - the same reflection
    /// technique as Controls.PreFlight.CheckListItem.GetValueObject
    /// ((double)Convert.ChangeType(Item.GetValue(defaultsrc, null), ...)), but the source is
    /// constructor-injected instead of read from a mutable static field, so this is unit-testable with a
    /// plain `new CurrentState { armed = false }` and no MAVLink connection. Real usage injects
    /// MainV2.comPort.MAV.cs (a CurrentState instance).
    /// </summary>
    public class TelemetryValueProvider : IValueProvider
    {
        readonly object _source;

        public TelemetryValueProvider(object source)
        {
            _source = source;
        }

        public bool TryGetValue(string field, out object value)
        {
            value = null;
            if (_source == null || string.IsNullOrWhiteSpace(field))
                return false;

            var property = _source.GetType().GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return false;

            try
            {
                value = property.GetValue(_source, null);
                return value != null;
            }
            catch (TargetInvocationException)
            {
                return false;
            }
        }
    }
}
