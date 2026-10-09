using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class NamedValueSample
    {
        public float Value { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime LastFiniteUtc { get; set; }
        public float LastFiniteValue { get; set; } = float.NaN;
    }

    public sealed class NamedValueTracker
    {
        const int MaxEntries = 1024;

        readonly object _sync = new object();
        readonly Dictionary<string, NamedValueSample> _samples = new Dictionary<string, NamedValueSample>(StringComparer.Ordinal);

        public void Record(int sysId, string name, float value, DateTime receivedUtc)
        {
            var normalized = AlertSource.NormalizeName(name);
            if (sysId <= 0 || normalized.Length == 0) return;
            var key = Key(sysId, normalized);
            lock (_sync)
            {
                if (!_samples.TryGetValue(key, out var sample))
                {
                    if (_samples.Count >= MaxEntries) return;
                    sample = new NamedValueSample { LastFiniteUtc = DateTime.MinValue };
                    _samples[key] = sample;
                }
                sample.Value = value;
                sample.LastSeenUtc = receivedUtc;
                if (!float.IsNaN(value) && !float.IsInfinity(value))
                {
                    sample.LastFiniteUtc = receivedUtc;
                    sample.LastFiniteValue = value;
                }
            }
        }

        public NamedValueSample Get(int sysId, string name)
        {
            var normalized = AlertSource.NormalizeName(name);
            if (sysId <= 0 || normalized.Length == 0) return null;
            lock (_sync)
            {
                if (!_samples.TryGetValue(Key(sysId, normalized), out var sample)) return null;
                return new NamedValueSample
                {
                    Value = sample.Value,
                    LastSeenUtc = sample.LastSeenUtc,
                    LastFiniteUtc = sample.LastFiniteUtc,
                    LastFiniteValue = sample.LastFiniteValue
                };
            }
        }

        public void Clear()
        {
            lock (_sync) _samples.Clear();
        }

        static string Key(int sysId, string normalized) => sysId + ":" + normalized;
    }
}
