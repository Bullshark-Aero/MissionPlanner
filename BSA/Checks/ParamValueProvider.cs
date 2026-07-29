using System;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Wraps a downloaded-parameters lookup for source: Param checks - the same underlying data as
    /// Controls.PreFlight.CheckListItem.HandleParam() (MAV.param[name].Value), but without that method's
    /// regex-out-of-a-free-text-Description lookup: the param name is just the JSON Field value
    /// directly. Takes a plain lookup delegate rather than the concrete MAVLink param collection type, so
    /// it's unit-testable with an in-memory dictionary. Real usage:
    /// new ParamValueProvider(name => MAV.param.ContainsKey(name)
    ///     ? (true, (double)MAV.param[name].Value) : (false, 0.0))
    /// </summary>
    public class ParamValueProvider : IValueProvider
    {
        readonly Func<string, (bool found, double value)> _lookup;

        public ParamValueProvider(Func<string, (bool found, double value)> lookup)
        {
            _lookup = lookup;
        }

        public bool TryGetValue(string field, out object value)
        {
            value = null;
            if (_lookup == null || string.IsNullOrWhiteSpace(field))
                return false;

            var result = _lookup(field);
            if (!result.found)
                return false;

            value = result.value;
            return true;
        }
    }
}
