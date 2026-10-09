using System.Collections.Generic;

namespace MissionPlanner.Warnings
{
    public enum AlertKind
    {
        Alarm,
        Readout
    }

    public enum AlertPhase
    {
        Any,
        Armed,
        Airborne,
        Hover,
        Forward
    }

    public enum AlertPhaseUnknown
    {
        AlertAnyway,
        StayQuiet
    }

    public enum AlertPreflight
    {
        None,
        Warning,
        Critical
    }

    public class AlertOptions
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool Enabled { get; set; } = true;
        public AlertKind Kind { get; set; }
        public string Source { get; set; }
        public List<string> SourceNames { get; set; } = new List<string>();
        public bool Always { get; set; }
        public double HoldSeconds { get; set; }
        public double FreshSeconds { get; set; } = 2;
        public double ArmedGraceSeconds { get; set; }
        public AlertPhase Phase { get; set; }
        public AlertPhaseUnknown WhenPhaseUnknown { get; set; }
        public bool TriggerOnStale { get; set; }
        public string StaleText { get; set; }
        public bool Show { get; set; }
        public AlertPreflight Preflight { get; set; }
    }
}
