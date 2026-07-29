using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ConfigDiffGroupTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>
            {
                new KeyPolicyRule { Match = "guided_alt*", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "hud*", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "distunits|speedunits|altunits", Class = KeyClass.Portable }
            },
            Default = KeyClass.MachineSpecific
        };

        [TestMethod]
        public void CoupledPair_SameRule_EndsUpInOneGroup()
        {
            // The exact real-world pair the WP2 Phase B pressure test found: guided_alt and
            // guided_alt_frame are written together (FlightData.cs) and must never be split.
            var result = new ConfigCompareResult();
            result.MismatchedKeys.Add("guided_alt");
            result.MismatchedKeys.Add("guided_alt_frame");

            var groups = ConfigDiffGrouping.Group(result, Policy());

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(2, groups[0].MismatchedKeys.Count);
            CollectionAssert.Contains(groups[0].MismatchedKeys, "guided_alt");
            CollectionAssert.Contains(groups[0].MismatchedKeys, "guided_alt_frame");
        }

        [TestMethod]
        public void SecondCoupledPair_HudBatteryCell_OneGroup()
        {
            var result = new ConfigCompareResult();
            result.MismatchedKeys.Add("HUD_batterycellcount");
            result.MismatchedKeys.Add("HUD_showbatterycell");

            var groups = ConfigDiffGrouping.Group(result, Policy());

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(2, groups[0].MismatchedKeys.Count);
        }

        [TestMethod]
        public void UnrelatedRules_StayInSeparateGroups()
        {
            var result = new ConfigCompareResult();
            result.MismatchedKeys.Add("guided_alt");
            result.MismatchedKeys.Add("HUD_showbatterycell");

            var groups = ConfigDiffGrouping.Group(result, Policy());

            Assert.AreEqual(2, groups.Count, "Keys matched by different rules must not merge into one group.");
        }

        [TestMethod]
        public void UnclassifiedKeys_FallToDefaultGroup_TogetherNotSplit()
        {
            var result = new ConfigCompareResult();
            result.MismatchedKeys.Add("some_unknown_key_1");
            result.MismatchedKeys.Add("some_unknown_key_2");

            var groups = ConfigDiffGrouping.Group(result, Policy());

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(ConfigDiffGrouping.UngroupedKey, groups[0].GroupKey);
        }

        [TestMethod]
        public void ApplicableKeys_ExcludesLiveOnly_IncludesMismatchedAndPackageOnly()
        {
            var result = new ConfigCompareResult();
            result.MismatchedKeys.Add("guided_alt");
            result.LiveOnlyKeys.Add("guided_alt_frame"); // hypothetical: package doesn't have this one

            var groups = ConfigDiffGrouping.Group(result, Policy());
            var applicable = new List<string>(groups[0].ApplicableKeys);

            CollectionAssert.Contains(applicable, "guided_alt");
            CollectionAssert.DoesNotContain(applicable, "guided_alt_frame");
        }

        [TestMethod]
        public void EmptyCompareResult_ProducesNoGroups()
        {
            var groups = ConfigDiffGrouping.Group(new ConfigCompareResult(), Policy());
            Assert.AreEqual(0, groups.Count);
        }
    }
}
