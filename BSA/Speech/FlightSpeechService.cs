using System;
using System.Diagnostics;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Speech
{
    /// <summary>Reads existing packet cache only. Never requests additional telemetry.</summary>
    internal sealed class FlightSpeechService
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FlightSpeech speech = new FlightSpeech();
        private object vehicle;
        private double nextTick;

        public void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now < nextTick) return;
            nextTick = now + 0.1;
            var port = MainV2.comPort;
            var mav = port.MAV;
            if (!ReferenceEquals(vehicle, mav))
            {
                vehicle = mav;
                speech.Reset();
            }
            if (!port.BaseStream.IsOpen || port.logreadmode || !MainV2.speechEnabled())
            {
                speech.Reset();
                return;
            }

            var utc = DateTime.UtcNow;
            var heartbeat = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT);
            var extended = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);
            var speedPacket = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.VFR_HUD);
            var rpmPacket = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.RPM);
            bool airborne = false;
            bool forward = false;
            if (Fresh(heartbeat, utc, 3) && Fresh(extended, utc, 3))
            {
                var hb = heartbeat.ToStructure<MAVLink.mavlink_heartbeat_t>();
                var state = extended.ToStructure<MAVLink.mavlink_extended_sys_state_t>();
                // MAV_LANDED_STATE IN_AIR/TAKEOFF/LANDING; MAV_MODE_FLAG SAFETY_ARMED.
                airborne = FlightSpeech.IsAirborne(hb.base_mode, state.landed_state);
                // MAV_VTOL_STATE TRANSITION_TO_FW/TRANSITION_TO_MC/FW.
                forward = FlightSpeech.IsForwardOrTransition(state.vtol_state);
            }
            double? speed = Fresh(speedPacket, utc, 2)
                ? (double?)speedPacket.ToStructure<MAVLink.mavlink_vfr_hud_t>().airspeed : null;
            // One low sample must not age into a confirmed failure. A continued stream is required.
            double? rpm = Fresh(rpmPacket, utc, 2)
                ? (double?)rpmPacket.ToStructure<MAVLink.mavlink_rpm_t>().rpm1 : null;
            var text = speech.Update(now, airborne, forward, speed, rpm,
                Settings.Instance.GetBoolean("speechflightairspeedenabled"),
                Settings.Instance.GetBoolean("speechenginefailureenabled"), MainV2.speechEngine.IsReady,
                rpmPacket?.rxtime.Ticks);
            if (text != null) MainV2.speechEngine.SpeakAsync(text);
        }

        private static bool Fresh(MAVLink.MAVLinkMessage packet, DateTime utc, double limit)
        {
            if (packet == null) return false;
            return FlightSpeech.IsFresh(packet.rxtime, utc, limit);
        }
    }
}
