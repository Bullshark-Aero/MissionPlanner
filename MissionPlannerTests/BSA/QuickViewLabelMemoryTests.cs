using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// An operator's label for a field is remembered so that cancelling the label prompt can restore it
    /// rather than fall back to the field's bare name. The record is keyed to the FIELD, not to the view:
    /// keyed to the view it would be restored over whatever field is chosen next, which is the
    /// wrong-caption-over-wrong-data defect this panel was already fixed for once.
    /// </summary>
    [TestClass]
    public class QuickViewLabelMemoryTests
    {
        static readonly MethodInfo Remember = typeof(FlightData)
            .GetMethod("rememberQuickViewLabel", BindingFlags.NonPublic | BindingFlags.Static);

        static readonly MethodInfo Recall = typeof(FlightData)
            .GetMethod("rememberedQuickViewLabel", BindingFlags.NonPublic | BindingFlags.Static);

        static readonly MethodInfo KeyFor = typeof(FlightData)
            .GetMethod("quickViewLabelMemoryKey", BindingFlags.NonPublic | BindingFlags.Static);

        Dictionary<string, string> _originalConfig;

        static void Save(string field, string label)
        {
            Remember.Invoke(null, new object[] {field, label});
        }

        static string Load(string field)
        {
            return (string) Recall.Invoke(null, new object[] {field});
        }

        static string Key(string field)
        {
            return (string) KeyFor.Invoke(null, new object[] {field});
        }

        [TestInitialize]
        public void SaveConfig()
        {
            var _ = Settings.Instance;
            _originalConfig = new Dictionary<string, string>(Settings.config);
            CurrentState.custom_field_names = new Dictionary<string, string>();

            // Settings.Instance is the machine's real configuration, so a field the operator has labelled
            // for real already has a record here. Start every test from an empty memory rather than
            // assuming absence - otherwise "no record" cases pass or fail depending on whose machine runs
            // them. TestCleanup puts the operator's records back.
            foreach (var key in new List<string>(Settings.config.Keys))
            {
                if (key.StartsWith("quickViewLabel_"))
                    Settings.config.Remove(key);
            }
        }

        [TestCleanup]
        public void RestoreConfig()
        {
            Settings.config.Clear();
            foreach (var kv in _originalConfig)
                Settings.config[kv.Key] = kv.Value;
        }

        [TestMethod]
        public void PlainField_RoundTrips()
        {
            Save("airspeed", "AIRSPEED (m/s)");

            Assert.AreEqual("AIRSPEED (m/s)", Load("airspeed"));
        }

        [TestMethod]
        public void FieldNeverLabelled_HasNoRecord()
        {
            // null is the signal for "fall back to the field's default name"
            Assert.IsNull(Load("groundspeed"));
        }

        [TestMethod]
        public void NamedValueField_IsRememberedByMavName_NotBySlot()
        {
            CurrentState.custom_field_names["customfield2"] = "MAV_THRUST";
            Save("customfield2", "THRUST (KG)");

            // the key must name the field, not the slot it happens to occupy this session
            Assert.AreEqual("quickViewLabel_MAV_THRUST", Key("customfield2"));
        }

        [TestMethod]
        public void RememberedLabel_SurvivesASlotReshuffle()
        {
            CurrentState.custom_field_names["customfield2"] = "MAV_THRUST";
            Save("customfield2", "THRUST (KG)");

            // restart: the vehicle sends its named values in a different order, so THRUST lands elsewhere
            CurrentState.custom_field_names = new Dictionary<string, string>
            {
                {"customfield0", "MAV_AS2"},
                {"customfield5", "MAV_THRUST"}
            };

            Assert.AreEqual("THRUST (KG)", Load("customfield5"));
            // and the label must NOT follow the slot to a different measurement
            Assert.IsNull(Load("customfield0"));
        }

        [TestMethod]
        public void SeparateFields_KeepSeparateRecords()
        {
            Save("airspeed", "AIRSPEED (m/s)");
            Save("groundspeed", "GROUNDSPEED");

            Assert.AreEqual("AIRSPEED (m/s)", Load("airspeed"));
            Assert.AreEqual("GROUNDSPEED", Load("groundspeed"));
        }

        [TestMethod]
        public void RelabellingAField_ReplacesItsRecord()
        {
            Save("airspeed", "OLD");
            Save("airspeed", "NEW");

            Assert.AreEqual("NEW", Load("airspeed"));
        }

        [TestMethod]
        public void BlankLabel_IsNotRecorded()
        {
            Save("airspeed", "GOOD");
            Save("airspeed", "   ");

            // whitespace is not a label; it must not wipe a good record and must not become one
            Assert.AreEqual("GOOD", Load("airspeed"));
        }

        [TestMethod]
        public void BlankOrMissingField_IsIgnoredRatherThanThrowing()
        {
            Save(null, "SOMETHING");
            Save("", "SOMETHING");

            Assert.IsNull(Load(null));
            Assert.IsNull(Load(""));
        }

        [TestMethod]
        public void WhitespaceStoredByHand_ReadsAsNoRecord()
        {
            // a hand-edited config should degrade to "use the default", not to a blank caption
            Settings.Instance[Key("airspeed")] = "   ";

            Assert.IsNull(Load("airspeed"));
        }
    }
}
