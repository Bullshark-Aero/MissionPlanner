using System;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class AlertContext
    {
        public DateTime NowUtc { get; set; }
        public bool Armed { get; set; }
        public bool PhaseKnown { get; set; }
        public byte LandedState { get; set; }
        public byte VtolState { get; set; }
    }

    public static class FlightPhase
    {
        public const double PhaseFreshSeconds = 3;

        public static bool IsAirborne(byte landedState) => landedState >= 2 && landedState <= 4;

        public static bool IsHover(byte vtolState) => vtolState == 3;

        public static bool IsForwardOrTransition(byte vtolState) => vtolState == 1 || vtolState == 2 || vtolState == 4;

        public static bool NeedsPhase(AlertPhase phase) =>
            phase == AlertPhase.Airborne || phase == AlertPhase.Hover || phase == AlertPhase.Forward;

        public static bool Allows(AlertOptions alert, AlertContext context)
        {
            if (alert == null || context == null) return false;
            switch (alert.Phase)
            {
                case AlertPhase.Any:
                    return true;
                case AlertPhase.Armed:
                    return context.Armed;
            }
            if (!context.Armed) return false;
            if (!context.PhaseKnown) return alert.WhenPhaseUnknown == AlertPhaseUnknown.AlertAnyway;
            if (!IsAirborne(context.LandedState)) return false;
            switch (alert.Phase)
            {
                case AlertPhase.Airborne: return true;
                case AlertPhase.Hover: return IsHover(context.VtolState);
                case AlertPhase.Forward: return IsForwardOrTransition(context.VtolState);
                default: return false;
            }
        }

        public static AlertPhaseUnknown DefaultUnknownRule(AlertKind kind) =>
            kind == AlertKind.Alarm ? AlertPhaseUnknown.AlertAnyway : AlertPhaseUnknown.StayQuiet;
    }
}
