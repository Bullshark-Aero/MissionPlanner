using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Log;
using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class LogSortTests
    {
        [TestMethod]
        public void SortLogsPrefersAutopilotWhenAdsbHeartbeatIsLast()
        {
            AssertSortedAsFixedWing(new[]
            {
                Heartbeat(MAVLink.MAV_TYPE.ADSB, MAVLink.MAV_AUTOPILOT.INVALID, 1, 0),
                Heartbeat(MAVLink.MAV_TYPE.FIXED_WING, MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, 1, 1)
            });
        }

        [TestMethod]
        public void SortLogsPrefersAutopilotWhenAutopilotHeartbeatIsLast()
        {
            AssertSortedAsFixedWing(new[]
            {
                Heartbeat(MAVLink.MAV_TYPE.FIXED_WING, MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, 1, 1),
                Heartbeat(MAVLink.MAV_TYPE.ADSB, MAVLink.MAV_AUTOPILOT.INVALID, 1, 0)
            });
        }

        [TestMethod]
        public void SortLogsPrefersAutopilotOverOnboardController()
        {
            AssertSortedAsFixedWing(new[]
            {
                Heartbeat(MAVLink.MAV_TYPE.ONBOARD_CONTROLLER, MAVLink.MAV_AUTOPILOT.INVALID, 1, 191),
                Heartbeat(MAVLink.MAV_TYPE.FIXED_WING, MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, 1, 1)
            });
        }

        [TestMethod]
        public void SortLogsKeepsLegacyFallbackWhenNoVehicleHeartbeatExists()
        {
            var root = CreateTemporaryDirectory();

            try
            {
                var input = WriteTlog(root, new[]
                {
                    Heartbeat(MAVLink.MAV_TYPE.ADSB, MAVLink.MAV_AUTOPILOT.INVALID, 1, 0)
                });

                LogSort.SortLogs(new[] {input}, root);

                Assert.IsTrue(File.Exists(Path.Combine(root, "ADSB", "1", Path.GetFileName(input))));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void AssertSortedAsFixedWing(IReadOnlyList<byte[]> heartbeatPackets)
        {
            var root = CreateTemporaryDirectory();

            try
            {
                var input = WriteTlog(root, heartbeatPackets);

                LogSort.SortLogs(new[] {input}, root);

                Assert.IsTrue(File.Exists(Path.Combine(root, "FIXED_WING", "1", Path.GetFileName(input))));
                Assert.IsFalse(Directory.Exists(Path.Combine(root, "ADSB")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "mission-planner-log-sort-" + Guid.NewGuid());
            Directory.CreateDirectory(path);
            return path;
        }

        private static string WriteTlog(string root, IReadOnlyList<byte[]> heartbeatPackets)
        {
            var path = Path.Combine(root, "sort-test.tlog");
            var timestamp = BitConverter.GetBytes((ulong) 1_800_000_000_000_000);
            Array.Reverse(timestamp);

            using (var stream = File.Create(path))
            {
                // LogSort ignores files at or below 1 KiB. Repeating the sequence also models
                // the alternating component heartbeats seen in the Judicar telemetry log.
                for (var index = 0; index < 64; index++)
                {
                    stream.Write(timestamp, 0, timestamp.Length);
                    var packet = heartbeatPackets[index % heartbeatPackets.Count];
                    stream.Write(packet, 0, packet.Length);
                }
            }

            return path;
        }

        private static byte[] Heartbeat(MAVLink.MAV_TYPE type, MAVLink.MAV_AUTOPILOT autopilot,
            byte sysid, byte compid)
        {
            var heartbeat = new MAVLink.mavlink_heartbeat_t
            {
                type = (byte) type,
                autopilot = (byte) autopilot,
                mavlink_version = 3
            };

            return new MAVLink.MavlinkParse().GenerateMAVLinkPacket20(
                MAVLink.MAVLINK_MSG_ID.HEARTBEAT, heartbeat, false, sysid, compid);
        }
    }
}
