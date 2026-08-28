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
