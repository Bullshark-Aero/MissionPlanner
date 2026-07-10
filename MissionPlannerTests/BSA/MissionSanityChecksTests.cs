using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class MissionSanityChecksTests
    {
        static Locationwp Wp(double lat, double lng, float alt, ushort id) => new Locationwp().Set(lat, lng, alt, id);

        [TestMethod]
        public void NonEmpty_NoWaypoints_Fails()
        {
            var (outcome, _) = MissionSanityChecks.EvaluateNonEmpty(new Dictionary<int, Locationwp>());
            Assert.AreEqual(CheckOutcome.Fail, outcome);
        }

        [TestMethod]
        public void NonEmpty_HasWaypoints_Passes()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var (outcome, _) = MissionSanityChecks.EvaluateNonEmpty(wps);
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }

        [TestMethod]
        public void HasTakeoffAndLanding_BothPresent_Passes()
        {
            var wps = new Dictionary<int, Locationwp>
            {
                [0] = Wp(1, 1, 0, (ushort)MAVLink.MAV_CMD.TAKEOFF),
                [1] = Wp(1, 1, 0, 16),
                [2] = Wp(1, 1, 0, (ushort)MAVLink.MAV_CMD.LAND)
            };
            var (outcome, _) = MissionSanityChecks.EvaluateHasTakeoffAndLanding(wps);
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }

        [TestMethod]
        public void HasTakeoffAndLanding_VtolVariants_Passes()
        {
            var wps = new Dictionary<int, Locationwp>
            {
                [0] = Wp(1, 1, 0, (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF),
                [1] = Wp(1, 1, 0, (ushort)MAVLink.MAV_CMD.VTOL_LAND)
            };
            var (outcome, _) = MissionSanityChecks.EvaluateHasTakeoffAndLanding(wps);
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }

        [TestMethod]
        public void HasTakeoffAndLanding_MissingLanding_Fails()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, (ushort)MAVLink.MAV_CMD.TAKEOFF) };
            var (outcome, detail) = MissionSanityChecks.EvaluateHasTakeoffAndLanding(wps);
            Assert.AreEqual(CheckOutcome.Fail, outcome);
            StringAssert.Contains(detail, "landing");
        }

        [TestMethod]
        public void HasTakeoffAndLanding_EmptyMission_IsUnknown()
        {
            var (outcome, _) = MissionSanityChecks.EvaluateHasTakeoffAndLanding(new Dictionary<int, Locationwp>());
            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }

        [TestMethod]
        public void HomePlausible_CloseToCurrentPosition_Passes()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(-35.363, 149.165, 0, 16) };
            var current = new PointLatLngAlt(-35.363, 149.165);
            var (outcome, _) = MissionSanityChecks.EvaluateHomePlausible(wps, current);
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }

        [TestMethod]
        public void HomePlausible_FarFromCurrentPosition_Fails()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(-35.363, 149.165, 0, 16) };
            var current = new PointLatLngAlt(51.5, -0.1); // thousands of km from the mission home
            var (outcome, _) = MissionSanityChecks.EvaluateHomePlausible(wps, current);
            Assert.AreEqual(CheckOutcome.Fail, outcome);
        }

        [TestMethod]
        public void HomePlausible_NoHomeWaypoint_IsUnknown()
        {
            var (outcome, _) = MissionSanityChecks.EvaluateHomePlausible(new Dictionary<int, Locationwp>(), new PointLatLngAlt(1, 1));
            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }

        [TestMethod]
        public void HomePlausible_NoCurrentPosition_IsUnknown()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var (outcome, _) = MissionSanityChecks.EvaluateHomePlausible(wps, null);
            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }

        [TestMethod]
        public void Unchanged_NoBaseline_IsUnknown()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var (outcome, _) = MissionSanityChecks.EvaluateUnchanged(wps, null);
            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }

        [TestMethod]
        public void Unchanged_SameMission_Passes()
        {
            var wps = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var baseline = MissionSanityChecks.HashWaypoints(wps);
            var (outcome, _) = MissionSanityChecks.EvaluateUnchanged(wps, baseline);
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }

        [TestMethod]
        public void Unchanged_EditedMission_Fails()
        {
            var original = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var baseline = MissionSanityChecks.HashWaypoints(original);

            var edited = new Dictionary<int, Locationwp> { [0] = Wp(2, 2, 0, 16) };
            var (outcome, _) = MissionSanityChecks.EvaluateUnchanged(edited, baseline);
            Assert.AreEqual(CheckOutcome.Fail, outcome);
        }

        [TestMethod]
        public void HashWaypoints_IndependentOfEnumerationOrder()
        {
            var a = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16), [1] = Wp(2, 2, 0, 16) };
            var b = new Dictionary<int, Locationwp> { [1] = Wp(2, 2, 0, 16), [0] = Wp(1, 1, 0, 16) };
            Assert.AreEqual(MissionSanityChecks.HashWaypoints(a), MissionSanityChecks.HashWaypoints(b));
        }

        [TestMethod]
        public void PointsPresent_None_Fails()
        {
            var (outcome, _) = MissionSanityChecks.EvaluatePointsPresent(new Dictionary<int, Locationwp>(), "fence");
            Assert.AreEqual(CheckOutcome.Fail, outcome);
        }

        [TestMethod]
        public void PointsPresent_Some_Passes()
        {
            var points = new Dictionary<int, Locationwp> { [0] = Wp(1, 1, 0, 16) };
            var (outcome, _) = MissionSanityChecks.EvaluatePointsPresent(points, "rally");
            Assert.AreEqual(CheckOutcome.Pass, outcome);
        }
    }
}
