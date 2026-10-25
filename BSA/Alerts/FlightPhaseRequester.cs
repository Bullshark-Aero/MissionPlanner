using System;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class FlightPhaseRequester
    {
        public const double RetrySeconds = 5;
        public const uint ExtendedSysStateMessageId = 245;
        public const float IntervalMicroseconds = 500000;

        readonly Action<int> _send;
        DateTime _lastSentUtc = DateTime.MinValue;
        int _lastSysId = -1;

        public FlightPhaseRequester(Action<int> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public bool Tick(DateTime nowUtc, bool needed, bool linkOpen, int sysId, bool phaseKnown)
        {
            if (!linkOpen || sysId <= 0)
            {
                _lastSentUtc = DateTime.MinValue;
                _lastSysId = -1;
                return false;
            }
            if (sysId != _lastSysId)
            {
                _lastSentUtc = DateTime.MinValue;
                _lastSysId = sysId;
            }
            if (!needed || phaseKnown) return false;
            if (_lastSentUtc != DateTime.MinValue && (nowUtc - _lastSentUtc).TotalSeconds < RetrySeconds) return false;
            _lastSentUtc = nowUtc;
            _send(sysId);
            return true;
        }

        public static MAVLink.mavlink_command_long_t Command(int sysId) => new MAVLink.mavlink_command_long_t
        {
            target_system = (byte)sysId,
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
            command = (ushort)MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL,
            param1 = ExtendedSysStateMessageId,
            param2 = IntervalMicroseconds
        };
    }
}
