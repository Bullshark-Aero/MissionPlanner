using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaQuickViewCodecTests
    {
        [TestMethod]
        public void ExportAndApply_UsesStableNamedValueIdentity()
        {
            var settings = new Dictionary<string, string>
            {
                ["quickViewRows"] = "1", ["quickViewCols"] = "2",
                ["quickView1"] = "customfield4", ["quickView1_label"] = "ESC hot",
                ["quickView2"] = "alt", ["quickView2_blank"] = "True"
            };
            var profile = BsaQuickViewCodec.Export(settings,
                new Dictionary<string, string> { ["customfield4"] = "MAV_ESC_HOT" });

            Assert.AreEqual("MAV_ESC_HOT", profile.Cells[0].SourceId);
            var target = new Dictionary<string, string> { ["quickView1_labelcolor"] = "Red" };
            BsaQuickViewCodec.Apply(target, profile);

            Assert.AreEqual("MAV_ESC_HOT", target["quickView1"]);
            Assert.AreEqual("True", target["quickView2_blank"]);
            Assert.IsFalse(target.ContainsKey("quickView1_labelcolor"));
        }

        [TestMethod]
        public void Export_CarriesLabelMemory_ByStableFieldName()
        {
            var settings = new Dictionary<string, string>
            {
                ["quickViewRows"] = "1", ["quickViewCols"] = "1", ["quickView1"] = "alt",
                ["quickViewLabel_airspeed"] = "AS",
                ["quickViewLabel_MAV_ESC_HOT"] = "ESC TEMP",
                ["quickViewLabel_customfield4"] = "LIFT",
                ["quickViewLabel_customfield9"] = "orphan",
                ["quickViewLabel_bad-name"] = "x",
                ["quickViewLabel_groundspeed"] = "  "
            };

            var profile = BsaQuickViewCodec.Export(settings,
                new Dictionary<string, string> { ["customfield4"] = "MAV_LIFT_HDR" });

            Assert.AreEqual(3, profile.Labels.Count);
            Assert.AreEqual("AS", profile.Labels["airspeed"]);
            Assert.AreEqual("ESC TEMP", profile.Labels["MAV_ESC_HOT"]);
            Assert.AreEqual("LIFT", profile.Labels["MAV_LIFT_HDR"]);
        }

        [TestMethod]
        public void Apply_MergesLabelMemory_BundleWinsForItsFieldsOnly()
        {
            var profile = new BsaQuickViewProfile
            {
                Rows = 1, Columns = 1,
                Cells = { new BsaQuickViewCell { Position = 1, SourceId = "alt" } },
                Labels = { ["airspeed"] = "AS", ["MAV_ESC_HOT"] = "ESC TEMP" }
            };
            var target = new Dictionary<string, string>
            {
                ["quickViewLabel_airspeed"] = "Mine",
                ["quickViewLabel_alt"] = "Keep",
                ["quickViewLabel_MAV_ESC_HOT"] = "ESC TEMP"
            };

            var changed = BsaQuickViewCodec.Apply(target, profile);

            Assert.AreEqual("AS", target["quickViewLabel_airspeed"]);
            Assert.AreEqual("Keep", target["quickViewLabel_alt"]);
            CollectionAssert.Contains(changed, "quickViewLabel_airspeed");
            CollectionAssert.DoesNotContain(changed, "quickViewLabel_MAV_ESC_HOT");
            CollectionAssert.DoesNotContain(changed, "quickViewLabel_alt");
        }

        [TestMethod]
        public void Validate_RejectsUnsafeLabelMemory()
        {
            foreach (var bad in new[]
                     {
                         new KeyValuePair<string, string>("bad name", "x"),
                         new KeyValuePair<string, string>("a-b", "x"),
                         new KeyValuePair<string, string>("customfield3", "x"),
                         new KeyValuePair<string, string>("airspeed", ""),
                         new KeyValuePair<string, string>("airspeed", "line\nbreak"),
                         new KeyValuePair<string, string>("airspeed", new string('x', 101))
                     })
            {
                var profile = new BsaQuickViewProfile
                {
                    Rows = 1, Columns = 1,
                    Cells = { new BsaQuickViewCell { Position = 1, SourceId = "alt" } },
                    Labels = { [bad.Key] = bad.Value }
                };
                Assert.ThrowsException<InvalidOperationException>(() => BsaQuickViewCodec.Validate(profile),
                    "should reject '" + bad.Key + "' = '" + bad.Value + "'");
            }
        }

        [TestMethod]
        public void Apply_ProfileWithoutLabelMemory_LeavesExistingMemoryAlone()
        {
            var profile = new BsaQuickViewProfile
            {
                Rows = 1, Columns = 1,
                Cells = { new BsaQuickViewCell { Position = 1, SourceId = "alt" } },
                Labels = null
            };
            var target = new Dictionary<string, string> { ["quickViewLabel_alt"] = "Keep" };

            BsaQuickViewCodec.Apply(target, profile);

            Assert.AreEqual("Keep", target["quickViewLabel_alt"]);
        }

        [TestMethod]
        public void Export_UnresolvedCustomField_FailsClosed()
        {
            var settings = new Dictionary<string, string>
            {
                ["quickViewRows"] = "1", ["quickViewCols"] = "1", ["quickView1"] = "customfield9"
            };
            Assert.ThrowsException<InvalidOperationException>(() =>
                BsaQuickViewCodec.Export(settings, new Dictionary<string, string>()));
        }

        [TestMethod]
        public void OwnsSetting_CoversAllQuickViewPersistenceKeys()
        {
            Assert.IsTrue(BsaQuickViewCodec.OwnsSetting("quickView30_valuecolor"));
            Assert.IsTrue(BsaQuickViewCodec.OwnsSetting("quickViewLabel_MAV_ESC_HOT"));
            Assert.IsFalse(BsaQuickViewCodec.OwnsSetting("quickViewNotACell"));
        }
    }
}
