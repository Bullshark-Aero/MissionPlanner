using System;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Wraps a key/value settings lookup for source: MpConfig checks. Values are returned exactly as the
    /// underlying store holds them (a string, in real usage via Settings.Instance[key]) -
    /// ConditionEvaluator's numeric/string-fallback comparison is what interprets them (e.g.
    /// speechenable = "True"). Takes a plain lookup delegate rather than the concrete Settings type, so
    /// it's unit-testable with an in-memory dictionary. Real usage:
    /// new SettingsValueProvider(key => Settings.Instance[key])
    /// </summary>
    public class SettingsValueProvider : IValueProvider
    {
        readonly Func<string, string> _lookup;

        public SettingsValueProvider(Func<string, string> lookup)
        {
            _lookup = lookup;
        }

        public bool TryGetValue(string field, out object value)
        {
            value = null;
            if (_lookup == null || string.IsNullOrWhiteSpace(field))
                return false;

            var raw = _lookup(field);
            if (raw == null)
                return false;

            value = raw;
            return true;
        }
    }
}
