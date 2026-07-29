using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class CompareRowTests
    {
        static ConfigDiffGroup Group(string groupKey, IEnumerable<string> mismatched = null, IEnumerable<string> packageOnly = null, IEnumerable<string> liveOnly = null)
        {
            var group = new ConfigDiffGroup { GroupKey = groupKey };
            if (mismatched != null) group.MismatchedKeys.AddRange(mismatched);
            if (packageOnly != null) group.PackageOnlyKeys.AddRange(packageOnly);
            if (liveOnly != null) group.LiveOnlyKeys.AddRange(liveOnly);
            return group;
        }

        [TestMethod]
        public void EmptyGroupList_ProducesNoRows()
        {
            var rows = CompareRow.FromGroups(new List<ConfigDiffGroup>(), new Dictionary<string, string>(), new Dictionary<string, string>());
            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void NullGroupList_ProducesNoRows()
        {
            var rows = CompareRow.FromGroups(null, new Dictionary<string, string>(), new Dictionary<string, string>());
            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void MismatchedKey_ProducesChangedRow_WithBothValues()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var package = new Dictionary<string, string> { ["distunits"] = "1" };
            var groups = new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "distunits" }) };

            var rows = CompareRow.FromGroups(groups, live, package);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(CompareRowStatus.Changed, rows[0].Status);
            Assert.AreEqual("distunits", rows[0].Key);
            Assert.AreEqual("0", rows[0].CurrentValue);
            Assert.AreEqual("1", rows[0].PackageValue);
        }

        [TestMethod]
        public void PackageOnlyKey_ProducesOnlyInPackageRow_WithEmptyCurrentValue()
        {
            var package = new Dictionary<string, string> { ["speechenable"] = "1" };
            var groups = new List<ConfigDiffGroup> { Group("g1", packageOnly: new[] { "speechenable" }) };

            var rows = CompareRow.FromGroups(groups, new Dictionary<string, string>(), package);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(CompareRowStatus.OnlyInPackage, rows[0].Status);
            Assert.AreEqual("speechenable", rows[0].Key);
            Assert.AreEqual("", rows[0].CurrentValue);
            Assert.AreEqual("1", rows[0].PackageValue);
        }

        [TestMethod]
        public void LiveOnlyKey_ProducesOnlyOnThisMachineRow_WithEmptyPackageValue()
        {
            var live = new Dictionary<string, string> { ["comport"] = "COM5" };
            var groups = new List<ConfigDiffGroup> { Group("g1", liveOnly: new[] { "comport" }) };

            var rows = CompareRow.FromGroups(groups, live, new Dictionary<string, string>());

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(CompareRowStatus.OnlyOnThisMachine, rows[0].Status);
            Assert.AreEqual("comport", rows[0].Key);
            Assert.AreEqual("COM5", rows[0].CurrentValue);
            Assert.AreEqual("", rows[0].PackageValue);
        }

        [TestMethod]
        public void MissingValueForKey_ProducesEmptyStringNotNull()
        {
            // A key can appear as mismatched/package-only/live-only without an entry in the
            // corresponding dictionary in edge cases - must degrade to "" not throw or return null,
            // since the grid binds directly to these strings.
            var groups = new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "some_key" }) };

            var rows = CompareRow.FromGroups(groups, new Dictionary<string, string>(), new Dictionary<string, string>());

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("", rows[0].CurrentValue);
            Assert.AreEqual("", rows[0].PackageValue);
        }

        [TestMethod]
        public void MultipleGroups_AllKindsOfKeys_FlattenedAndSortedByKeyOrdinal()
        {
            var live = new Dictionary<string, string> { ["zebra"] = "z-live", ["comport"] = "COM5" };
            var package = new Dictionary<string, string> { ["zebra"] = "z-pkg", ["speechenable"] = "1" };
            var groups = new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "zebra" }, liveOnly: new[] { "comport" }),
                Group("g2", packageOnly: new[] { "speechenable" })
            };

            var rows = CompareRow.FromGroups(groups, live, package);

            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEqual(
                new[] { "comport", "speechenable", "zebra" },
                new[] { rows[0].Key, rows[1].Key, rows[2].Key },
                "Rows must be flattened out of their coupled groups and sorted alphabetically for a read-only report.");
        }

        [TestMethod]
        public void CoupledGroup_BothKeysPreserved_AsSeparateRows()
        {
            // Unlike ImportDiffPanel (which must keep a coupled pair as one checkable unit), a
            // read-only compare report has no apply semantics - each key gets its own row even when
            // grouped by the same policy rule.
            var live = new Dictionary<string, string> { ["guided_alt"] = "10", ["guided_alt_frame"] = "0" };
            var package = new Dictionary<string, string> { ["guided_alt"] = "20", ["guided_alt_frame"] = "1" };
            var groups = new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "guided_alt", "guided_alt_frame" }) };

            var rows = CompareRow.FromGroups(groups, live, package);

            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.Exists(r => r.Key == "guided_alt" && r.CurrentValue == "10" && r.PackageValue == "20"));
            Assert.IsTrue(rows.Exists(r => r.Key == "guided_alt_frame" && r.CurrentValue == "0" && r.PackageValue == "1"));
        }
    }
}
