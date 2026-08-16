using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.GCSViews;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// Quick view fields backed by named_value_float (MAV_*) live in whichever customfieldN slot the
    /// vehicle filled first, and that order is not stable across restarts. These cover the two helpers
    /// that keep the config pinned to the MAV_ name and resolve the slot at bind time instead.
    /// </summary>
    [TestClass]
    public class QuickViewFieldPersistenceTests
    {
        static readonly MethodInfo SettingValue = typeof(FlightData)
            .GetMethod("quickViewSettingValue", BindingFlags.NonPublic | BindingFlags.Static);

        static readonly MethodInfo Resolve = typeof(FlightData)
            .GetMethod("resolveQuickViewField", BindingFlags.NonPublic | BindingFlags.Static);

        static string ToSetting(string fieldName) => (string) SettingValue.Invoke(null, new object[] {fieldName});

        static string ToField(string saved) => (string) Resolve.Invoke(null, new object[] {saved});

        [TestInitialize]
        public void ResetCustomFields()
        {
            CurrentState.custom_field_names = new Dictionary<string, string>();
        }

        [TestMethod]
        public void NamedValueField_IsSavedUnderItsMavName_NotItsSlot()
        {
            CurrentState.custom_field_names["customfield7"] = "MAV_THRUST";

            Assert.AreEqual("MAV_THRUST", ToSetting("customfield7"));
        }

        [TestMethod]
        public void PlainCurrentStateField_IsSavedUnchanged()
        {
            Assert.AreEqual("groundspeed", ToSetting("groundspeed"));
        }

        [TestMethod]
        public void CustomFieldWithoutMavName_FallsBackToTheSlot()
        {
            // the tuning graph loader and plugins put non-MAV_ names in here
            CurrentState.custom_field_names["customfield9"] = "Latency";

            Assert.AreEqual("customfield9", ToSetting("customfield9"));
            Assert.AreEqual("customfield3", ToSetting("customfield3"));
        }

        [TestMethod]
        public void SavedMavName_ResolvesToWhicheverSlotItHoldsThisSession()
        {
            // same config, different arrival order after a restart
            CurrentState.custom_field_names["customfield0"] = "MAV_AS2";
            CurrentState.custom_field_names["customfield1"] = "MAV_THRUST";

            Assert.AreEqual("customfield1", ToField("MAV_THRUST"));

            ResetCustomFields();
            CurrentState.custom_field_names["customfield0"] = "MAV_THRUST";
            CurrentState.custom_field_names["customfield1"] = "MAV_AS2";

            Assert.AreEqual("customfield0", ToField("MAV_THRUST"));
        }

        [TestMethod]
        public void SavedMavName_IsUnresolvedUntilTheFieldArrives()
        {
            Assert.IsNull(ToField("MAV_THRUST"));

            CurrentState.custom_field_names["customfield4"] = "MAV_THRUST";

            Assert.AreEqual("customfield4", ToField("MAV_THRUST"));
        }

        [TestMethod]
        public void NonMavEntries_ResolveToThemselves()
        {
            Assert.AreEqual("groundspeed", ToField("groundspeed"));
            // legacy configs saved the raw slot - left bound as before, no silent migration
            Assert.AreEqual("customfield7", ToField("customfield7"));
            Assert.IsNull(ToField(null));
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsAcrossASlotReshuffle()
        {
            CurrentState.custom_field_names["customfield2"] = "MAV_THRUST";
            var saved = ToSetting("customfield2");

            // restart: the vehicle sends AS2 first this time
            ResetCustomFields();
            CurrentState.custom_field_names["customfield2"] = "MAV_AS2";
            CurrentState.custom_field_names["customfield5"] = "MAV_THRUST";

            Assert.AreEqual("customfield5", ToField(saved));
        }
    }
}
