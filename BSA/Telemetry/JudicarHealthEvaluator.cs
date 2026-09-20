using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Telemetry
{
    public class JudicarHealthValues
    {
        public float DataOk { get; set; }
        public float EscOk { get; set; }
        public float GpsRedundancyOk { get; set; }
    }

    /// <summary>Bounded, declarative health evaluation with no transport or stream-rate API.</summary>
    public sealed class JudicarHealthEvaluator
    {
        static readonly HashSet<string> AllowedKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "any-named-value-fresh", "finite-named-value-fresh", "not-exactly-one-gps-fix"
        };

        readonly object _sync = new object();
        readonly BsaHealthRuleSet _rules;
        readonly Dictionary<string, DateTime> _lastSeenUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> _lastFiniteUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        DateTime _armedSinceUtc = DateTime.MinValue;
        bool _wasArmed;

        public JudicarHealthEvaluator(BsaHealthRuleSet rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Validate(rules);
        }

        public void RecordNamedValue(string fieldId, float value, DateTime receivedUtc)
        {
            if (string.IsNullOrWhiteSpace(fieldId)) return;
            lock (_sync)
            {
                var normalized = Normalize(fieldId);
                _lastSeenUtc[normalized] = receivedUtc;
                if (!float.IsNaN(value) && !float.IsInfinity(value)) _lastFiniteUtc[normalized] = receivedUtc;
            }
        }

        public JudicarHealthValues Evaluate(bool armed, float gps1Status, float gps2Status, DateTime nowUtc)
        {
            if (!armed)
            {
                _wasArmed = false;
                _armedSinceUtc = DateTime.MinValue;
                return Healthy();
            }
            if (!_wasArmed)
            {
                _wasArmed = true;
                _armedSinceUtc = nowUtc;
            }
            if (_rules.Rules.Any(r => nowUtc - _armedSinceUtc < TimeSpan.FromSeconds(r.ArmedGraceSeconds)))
                return Healthy();

            var values = Healthy();
            foreach (var rule in _rules.Rules)
            {
                var ok = EvaluateRule(rule, gps1Status, gps2Status, nowUtc);
                switch (rule.OutputFieldId)
                {
                    case "J26_DATA_OK": values.DataOk = ok ? 1 : 0; break;
                    case "J26_ESC_OK": values.EscOk = ok ? 1 : 0; break;
                    case "J26_GPS_RED_OK": values.GpsRedundancyOk = ok ? 1 : 0; break;
                }
            }
            return values;
        }

        bool EvaluateRule(BsaHealthRule rule, float gps1Status, float gps2Status, DateTime nowUtc)
        {
            if (rule.Kind == "not-exactly-one-gps-fix") return !((gps1Status >= 3) ^ (gps2Status >= 3));
            lock (_sync)
            {
                var samples = rule.Kind == "finite-named-value-fresh" ? _lastFiniteUtc : _lastSeenUtc;
                return rule.InputFieldIds.Any(field =>
                    samples.TryGetValue(Normalize(field), out var seen) && nowUtc >= seen &&
                    nowUtc - seen <= TimeSpan.FromSeconds(rule.FreshnessSeconds));
            }
        }

        public static void Validate(BsaHealthRuleSet rules)
        {
            if (rules == null || rules.EvaluationHz <= 0 || rules.EvaluationHz > 4 || rules.Rules == null)
                throw new InvalidDataException("Health rules require a bounded evaluation rate no faster than 4 Hz.");
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules.Rules)
            {
                if (rule == null || !AllowedKinds.Contains(rule.Kind)) throw new InvalidDataException("Health rule kind is not allowed.");
                if (!outputs.Add(rule.OutputFieldId)) throw new InvalidDataException("Health output fields must be unique.");
                if (rule.ArmedGraceSeconds < 0 || rule.FreshnessSeconds < 0 || rule.InputFieldIds == null || rule.InputFieldIds.Count == 0)
                    throw new InvalidDataException("Health rule timing and inputs are invalid.");
                if (rule.OutputFieldId != "J26_DATA_OK" && rule.OutputFieldId != "J26_ESC_OK" && rule.OutputFieldId != "J26_GPS_RED_OK")
                    throw new InvalidDataException("Health output field is not approved.");
            }
        }

        static string Normalize(string field) => field.StartsWith("MAV_", StringComparison.OrdinalIgnoreCase) ? field.Substring(4) : field;
        static JudicarHealthValues Healthy() => new JudicarHealthValues { DataOk = 1, EscOk = 1, GpsRedundancyOk = 1 };
    }
}
