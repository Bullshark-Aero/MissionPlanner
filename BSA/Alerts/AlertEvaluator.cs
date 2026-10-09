using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public enum AlertMode
    {
        Quiet,
        Triggered,
        Stale
    }

    public sealed class AlertDecision
    {
        public CustomWarning Warning { get; set; }
        public AlertMode Mode { get; set; }
        public string SpeakText { get; set; }
        public bool IsAlarm { get; set; }
    }

    public sealed class AlertColourChange
    {
        public string Field { get; set; }
        public string Colour { get; set; }
    }

    public sealed class AlertTickResult
    {
        public List<AlertDecision> Decisions { get; } = new List<AlertDecision>();
        public List<AlertColourChange> Colours { get; } = new List<AlertColourChange>();
    }

    public sealed class AlertEvaluator
    {
        public const string NoColour = "NoColor";
        public const double ReadoutRepeatFloorSeconds = 3;
        public const double AlarmRepeatFloorSeconds = 1;

        sealed class State
        {
            public AlertMode Mode;
            public DateTime? TrueSinceUtc;
            public long FirstSampleKey;
            public DateTime NextSpeakUtc = DateTime.MinValue;
            public DateTime LastSpokenUtc = DateTime.MinValue;
            public string ColouredField;
        }

        sealed class ReferenceComparer : IEqualityComparer<CustomWarning>
        {
            public bool Equals(CustomWarning x, CustomWarning y) => ReferenceEquals(x, y);
            public int GetHashCode(CustomWarning obj) => RuntimeHelpers.GetHashCode(obj);
        }

        readonly object _sync = new object();
        readonly Dictionary<CustomWarning, State> _states = new Dictionary<CustomWarning, State>(new ReferenceComparer());
        DateTime? _armedSinceUtc;

        public AlertTickResult Evaluate(IEnumerable<CustomWarning> warnings, AlertContext context, Func<AlertOptions, AlertReading> read)
        {
            var result = new AlertTickResult();
            lock (_sync)
            {
                if (!context.Armed) _armedSinceUtc = null;
                else if (!_armedSinceUtc.HasValue) _armedSinceUtc = context.NowUtc;

                var live = new HashSet<CustomWarning>(new ReferenceComparer());
                foreach (var warning in warnings ?? Enumerable.Empty<CustomWarning>())
                {
                    if (warning?.Alert == null || !live.Add(warning)) continue;
                    if (!_states.TryGetValue(warning, out var state))
                    {
                        state = new State();
                        _states[warning] = state;
                    }
                    result.Decisions.Add(EvaluateOne(warning, state, context, read, result.Colours));
                }

                foreach (var gone in _states.Keys.Where(w => !live.Contains(w)).ToList())
                {
                    ClearColour(_states[gone], result.Colours);
                    _states.Remove(gone);
                }
            }
            return result;
        }

        public void MarkSpoken(CustomWarning warning, DateTime nowUtc)
        {
            if (warning?.Alert == null) return;
            lock (_sync)
            {
                if (!_states.TryGetValue(warning, out var state)) return;
                state.LastSpokenUtc = nowUtc;
                state.NextSpeakUtc = nowUtc.AddSeconds(RepeatSeconds(warning));
            }
        }

        public List<AlertColourChange> Reset()
        {
            var colours = new List<AlertColourChange>();
            lock (_sync)
            {
                foreach (var state in _states.Values) ClearColour(state, colours);
                _states.Clear();
                _armedSinceUtc = null;
            }
            return colours;
        }

        public static double RepeatSeconds(CustomWarning warning) =>
            Math.Max(RepeatFloorSeconds(warning.Alert), warning.RepeatTime);

        public static double RepeatFloorSeconds(AlertOptions alert) =>
            alert != null && alert.Kind == AlertKind.Readout ? ReadoutRepeatFloorSeconds : AlarmRepeatFloorSeconds;

        public static bool Compare(CustomWarning.Conditional condition, double lhs, double rhs)
        {
            if (double.IsNaN(lhs) || double.IsNaN(rhs)) return false;
            switch (condition)
            {
                case CustomWarning.Conditional.LT: return lhs < rhs;
                case CustomWarning.Conditional.LTEQ: return lhs <= rhs;
                case CustomWarning.Conditional.EQ: return lhs == rhs;
                case CustomWarning.Conditional.GT: return lhs > rhs;
                case CustomWarning.Conditional.GTEQ: return lhs >= rhs;
                case CustomWarning.Conditional.NEQ: return lhs != rhs;
                default: return false;
            }
        }

        public static string FormatText(CustomWarning warning, AlertReading reading)
        {
            var alert = warning.Alert;
            var value = "";
            if (reading != null && reading.Usable)
            {
                value = alert.Kind == AlertKind.Readout
                    ? Math.Round(reading.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
                    : reading.Value.ToString("0.##", CultureInfo.InvariantCulture);
            }
            return (warning.Text ?? "")
                .Replace("{value}", value)
                .Replace("{warning}", warning.Warning.ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("{name}", alert.Title ?? "")
                .Trim();
        }

        AlertDecision EvaluateOne(CustomWarning warning, State state, AlertContext context,
            Func<AlertOptions, AlertReading> read, List<AlertColourChange> colours)
        {
            var alert = warning.Alert;
            var decision = new AlertDecision { Warning = warning, Mode = AlertMode.Quiet };
            AlertReading reading = null;
            var mode = AlertMode.Quiet;

            if (alert.Enabled && FlightPhase.Allows(alert, context) && !InArmedGrace(alert, context))
            {
                reading = read(alert) ?? new AlertReading();
                bool candidate;
                if (!reading.Usable)
                {
                    candidate = alert.TriggerOnStale;
                    if (!candidate) mode = AlertMode.Stale;
                }
                else
                {
                    candidate = alert.Always || Compare(warning.ConditionType, reading.Value, warning.Warning);
                }

                if (candidate)
                {
                    if (!state.TrueSinceUtc.HasValue)
                    {
                        state.TrueSinceUtc = context.NowUtc;
                        state.FirstSampleKey = reading.SampleKey;
                    }
                    var held = (context.NowUtc - state.TrueSinceUtc.Value).TotalSeconds >= alert.HoldSeconds;
                    var distinct = alert.HoldSeconds <= 0 || !reading.Usable || reading.SampleKey != state.FirstSampleKey;
                    if (held && distinct) mode = AlertMode.Triggered;
                }
                else
                {
                    state.TrueSinceUtc = null;
                }
            }
            else
            {
                state.TrueSinceUtc = null;
            }

            if (mode != state.Mode)
            {
                state.Mode = mode;
                state.NextSpeakUtc = state.LastSpokenUtc == DateTime.MinValue
                    ? DateTime.MinValue
                    : state.LastSpokenUtc.AddSeconds(RepeatFloorSeconds(alert));
            }
            decision.Mode = mode;

            string text = null;
            if (mode == AlertMode.Triggered)
            {
                text = FormatText(warning, reading);
                decision.IsAlarm = alert.Kind == AlertKind.Alarm;
            }
            else if (mode == AlertMode.Stale)
            {
                text = (alert.StaleText ?? "").Trim();
                decision.IsAlarm = true;
            }
            if (!string.IsNullOrEmpty(text) && context.NowUtc >= state.NextSpeakUtc)
                decision.SpeakText = text;

            var field = reading?.DisplayField;
            var colour = (warning.color ?? "").Trim();
            if (mode == AlertMode.Triggered && alert.Show && !string.IsNullOrEmpty(field) &&
                colour.Length > 0 && colour != NoColour)
            {
                if (state.ColouredField != null && state.ColouredField != field) ClearColour(state, colours);
                state.ColouredField = field;
                colours.Add(new AlertColourChange { Field = field, Colour = colour });
            }
            else
            {
                ClearColour(state, colours);
            }

            return decision;
        }

        bool InArmedGrace(AlertOptions alert, AlertContext context) =>
            alert.ArmedGraceSeconds > 0 && _armedSinceUtc.HasValue &&
            (context.NowUtc - _armedSinceUtc.Value).TotalSeconds < alert.ArmedGraceSeconds;

        static void ClearColour(State state, List<AlertColourChange> colours)
        {
            if (state.ColouredField == null) return;
            colours.Add(new AlertColourChange { Field = state.ColouredField, Colour = NoColour });
            state.ColouredField = null;
        }
    }

    public static class AlertSpeechArbiter
    {
        public static AlertDecision Choose(IEnumerable<AlertDecision> decisions, bool speechReady, bool readoutSpeaking,
            out bool cancelFirst)
        {
            cancelFirst = false;
            var due = (decisions ?? Enumerable.Empty<AlertDecision>()).Where(d => d?.SpeakText != null).ToList();
            var alarm = due.FirstOrDefault(d => d.IsAlarm);
            if (alarm != null)
            {
                if (speechReady) return alarm;
                if (!readoutSpeaking) return null;
                cancelFirst = true;
                return alarm;
            }
            return speechReady ? due.FirstOrDefault() : null;
        }
    }
}
