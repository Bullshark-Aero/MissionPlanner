using System;
using System.Globalization;

namespace MissionPlanner.BSA.Speech
{
    /// <summary>Stateful speech decisions. Times are monotonic seconds, values are SI.
    /// Unknown or stale inputs must be passed as null/false by the caller.</summary>
    public sealed class FlightSpeech
    {
        public static bool IsAirborne(byte baseMode, byte landedState)
        {
            return (baseMode & 128) != 0 && landedState >= 2 && landedState <= 4;
        }

        public static bool IsForwardOrTransition(byte vtolState)
        {
            return vtolState == 1 || vtolState == 2 || vtolState == 4;
        }

        public static bool IsFresh(DateTime receivedUtc, DateTime utc, double limit)
        {
            double age = (utc - receivedUtc).TotalSeconds;
            return age >= 0 && age <= limit;
        }

        private double? lowSince;
        private long firstLowSample;
        private double nextEngine;
        private double nextSpeed;

        public void Reset()
        {
            lowSince = null;
            nextEngine = nextSpeed = 0;
        }

        public string Update(double now, bool airborne, bool forwardOrTransition,
            double? airspeed, double? rpm, bool speedEnabled, bool engineEnabled, bool ready,
            long? rpmSample = null)
        {
            if (!airborne)
            {
                Reset();
                return null;
            }

            if (engineEnabled && Valid(rpm) && rpm.Value < 1000)
            {
                if (!lowSince.HasValue)
                {
                    lowSince = now;
                    firstLowSample = rpmSample ?? (long)(now * 1000);
                }
            }
            else
            {
                lowSince = null;
                nextEngine = 0;
            }

            if (lowSince.HasValue && now - lowSince.Value > 1 &&
                (rpmSample ?? (long)(now * 1000)) > firstLowSample && now >= nextEngine && ready)
            {
                nextEngine = now + 10;
                nextSpeed = now + 3;
                return "Engine failure";
            }

            if (!speedEnabled || !forwardOrTransition || !Valid(airspeed))
            {
                nextSpeed = 0;
                return null;
            }

            if (ready && now >= nextSpeed)
            {
                nextSpeed = now + 3;
                return Math.Round(airspeed.Value, MidpointRounding.AwayFromZero)
                    .ToString("0", CultureInfo.InvariantCulture);
            }
            return null;
        }

        private static bool Valid(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) &&
                !double.IsInfinity(value.Value) && value.Value >= 0;
        }
    }
}
