using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.ArduPilot;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// The BSA airframe is a quadplane, so Mission Planner withholds the ArduPlane modes it cannot use
    /// from the command surfaces. ArduPilot.Common.getModesList must stay complete regardless - it is also what
    /// names incoming heartbeats, and a mode missing from it would leave the HUD reporting the previous
    /// mode after an RC-switch or failsafe change into something withheld.
    ///
    /// The filter is exercised against a synthetic list rather than through getModesList, whose contents
    /// come from firmware metadata downloaded into C:\ProgramData - asserting on that would make the
    /// result depend on which firmware the machine running the test last cached.
    /// </summary>
    [TestClass]
    public class VtolModeRestrictionTests
    {
        static readonly int[] Withheld = { 3, 4, 14, 16, 24 };

        // ArduPlane 4.7's FLTMODE1 options, plus INITIALISING as Common appends it.
        static List<KeyValuePair<int, string>> PlaneModes()
        {
            return new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, "Manual"),
                new KeyValuePair<int, string>(1, "CIRCLE"),
                new KeyValuePair<int, string>(2, "STABILIZE"),
                new KeyValuePair<int, string>(3, "TRAINING"),
                new KeyValuePair<int, string>(4, "ACRO"),
                new KeyValuePair<int, string>(5, "FBWA"),
                new KeyValuePair<int, string>(6, "FBWB"),
                new KeyValuePair<int, string>(7, "CRUISE"),
                new KeyValuePair<int, string>(8, "AUTOTUNE"),
                new KeyValuePair<int, string>(10, "Auto"),
                new KeyValuePair<int, string>(11, "RTL"),
                new KeyValuePair<int, string>(12, "Loiter"),
                new KeyValuePair<int, string>(13, "TAKEOFF"),
                new KeyValuePair<int, string>(14, "AVOID_ADSB"),
                new KeyValuePair<int, string>(15, "Guided"),
                new KeyValuePair<int, string>(16, "INITIALISING"),
                new KeyValuePair<int, string>(17, "QSTABILIZE"),
                new KeyValuePair<int, string>(18, "QHOVER"),
                new KeyValuePair<int, string>(19, "QLOITER"),
                new KeyValuePair<int, string>(20, "QLAND"),
                new KeyValuePair<int, string>(21, "QRTL"),
                new KeyValuePair<int, string>(22, "QAUTOTUNE"),
                new KeyValuePair<int, string>(23, "QACRO"),
                new KeyValuePair<int, string>(24, "THERMAL"),
                new KeyValuePair<int, string>(25, "Loiter to QLand"),
                new KeyValuePair<int, string>(26, "AUTOLAND")
            };
        }

        static List<int> FilteredKeys()
        {
            return ArduPilot.Common.filterToVtolCommandable(PlaneModes()).Select(m => m.Key).ToList();
        }

        [TestMethod]
        public void WithheldModes_AreNotCommandable()
        {
            var keys = FilteredKeys();

            foreach (var mode in Withheld)
                Assert.IsFalse(keys.Contains(mode), "mode " + mode + " should have been withheld");
        }

        [TestMethod]
        public void EveryOtherMode_Survives()
        {
            var keys = FilteredKeys();

            foreach (var mode in PlaneModes().Select(m => m.Key).Where(k => !Withheld.Contains(k)))
                Assert.IsTrue(keys.Contains(mode), "mode " + mode + " should still be commandable");

            Assert.AreEqual(21, keys.Count);
        }

        [TestMethod]
        public void EveryVtolMode_Survives()
        {
            var keys = FilteredKeys();

            // the seven Q modes plus Loiter to QLand - the whole point of the airframe
            foreach (var mode in new[] { 17, 18, 19, 20, 21, 22, 23, 25 })
                Assert.IsTrue(keys.Contains(mode), "VTOL mode " + mode + " must remain commandable");
        }

        [TestMethod]
        public void MissionAndRecoveryModes_Survive()
        {
            var keys = FilteredKeys();

            // AUTO/RTL/Loiter/Guided fly the transit phase; TAKEOFF and AUTOLAND are the fixed-wing
            // launch and recovery; Manual is required by the BSA preflight control-surface check
            foreach (var mode in new[] { 0, 10, 11, 12, 13, 15, 26 })
                Assert.IsTrue(keys.Contains(mode), "mode " + mode + " must remain commandable");
        }

        [TestMethod]
        public void Filter_InventsNothing()
        {
            var input = PlaneModes();
            var output = ArduPilot.Common.filterToVtolCommandable(input);

            foreach (var entry in output)
                Assert.IsTrue(input.Any(m => m.Key == entry.Key && m.Value == entry.Value),
                    "filter produced an entry that was not in the source list: " + entry.Value);
        }

        [TestMethod]
        public void Filter_DoesNotMutateItsInput()
        {
            var input = PlaneModes();
            ArduPilot.Common.filterToVtolCommandable(input);

            Assert.AreEqual(26, input.Count);
        }

        [TestMethod]
        public void Filter_HandlesNull()
        {
            Assert.IsNull(ArduPilot.Common.filterToVtolCommandable(null));
        }

        [TestMethod]
        public void NonPlaneFirmware_IsNotFiltered()
        {
            // the withheld numbers mean other things elsewhere - 3 is Auto and 4 is Guided on Copter -
            // so the filter must never reach a firmware it was not chosen for
            var full = ArduPilot.Common.getModesList(Firmwares.ArduCopter2);

            if (full == null || full.Count == 0)
                Assert.Inconclusive("no ArduCopter parameter metadata cached on this machine");

            var commandable = ArduPilot.Common.getCommandableModesList(Firmwares.ArduCopter2);

            CollectionAssert.AreEqual(full.Select(m => m.Key).ToList(),
                commandable.Select(m => m.Key).ToList());
        }

        [TestMethod]
        public void FullPlaneModeList_StaysUnfiltered()
        {
            // regression guard for the display path: naming a heartbeat's mode goes through
            // getModesList, so it has to keep the modes the command surfaces withhold
            var full = ArduPilot.Common.getModesList(Firmwares.ArduPlane);

            if (full == null || full.Count == 0)
                Assert.Inconclusive("no ArduPlane parameter metadata cached on this machine");

            foreach (var mode in Withheld)
                Assert.IsTrue(full.Any(m => m.Key == mode),
                    "getModesList lost mode " + mode + " - the HUD can no longer name it");
        }

        [TestMethod]
        public void CommandableList_IsShorterThanFullList()
        {
            var full = ArduPilot.Common.getModesList(Firmwares.ArduPlane);

            if (full == null || full.Count == 0)
                Assert.Inconclusive("no ArduPlane parameter metadata cached on this machine");

            Assert.IsTrue(ArduPilot.Common.getCommandableModesList(Firmwares.ArduPlane).Count < full.Count);
        }

        static MAVLinkInterface PlaneLink()
        {
            var link = new MAVLinkInterface();
            link.MAVlist[1, 1].cs.firmware = Firmwares.ArduPlane;
            return link;
        }

        [TestMethod]
        public void TranslateMode_RefusesWithheldMode()
        {
            var link = PlaneLink();
            var mode = new MAVLink.mavlink_set_mode_t();

            Assert.IsFalse(link.translateMode(1, 1, "ACRO", ref mode));
            Assert.AreEqual(0, mode.base_mode);
        }

        [TestMethod]
        public void TranslateMode_AcceptsRetainedModeWhateverTheCasing()
        {
            var link = PlaneLink();

            foreach (var name in new[] { "QLOITER", "qloiter", "QLoiter" })
            {
                var mode = new MAVLink.mavlink_set_mode_t();
                Assert.IsTrue(link.translateMode(1, 1, name, ref mode), name + " should be commandable");
                Assert.AreEqual(19u, mode.custom_mode);
            }
        }

        [TestMethod]
        public void TranslateMode_AcceptsTheNamesTheQuickButtonsSend()
        {
            // the literal strings passed by FlightData's action buttons and the guided map click -
            // these are what actually break if the filter or the metadata casing shifts
            var link = PlaneLink();

            foreach (var name in new[] { "Auto", "Loiter", "RTL", "GUIDED" })
            {
                var mode = new MAVLink.mavlink_set_mode_t();
                Assert.IsTrue(link.translateMode(1, 1, name, ref mode),
                    "quick action \"" + name + "\" no longer resolves to a commandable mode");
            }
        }
    }
}
