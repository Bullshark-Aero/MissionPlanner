using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ImportDiffPanelTests
    {
        /// <summary>Populate with empty value dictionaries - these tests exercise selection
        /// mechanics, not the value-preview text.</summary>
        static void Populate(ImportDiffPanel panel, List<ConfigDiffGroup> groups) =>
            panel.Populate(groups, new Dictionary<string, string>(), new Dictionary<string, string>());

        static ConfigDiffGroup Group(string groupKey, IEnumerable<string> mismatched = null, IEnumerable<string> packageOnly = null, IEnumerable<string> liveOnly = null)
        {
            var group = new ConfigDiffGroup { GroupKey = groupKey };
            if (mismatched != null) group.MismatchedKeys.AddRange(mismatched);
            if (packageOnly != null) group.PackageOnlyKeys.AddRange(packageOnly);
            if (liveOnly != null) group.LiveOnlyKeys.AddRange(liveOnly);
            return group;
        }

        [TestMethod]
        public void Populate_NothingChecked_GetSelectedKeysIsEmpty()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "distunits" }) });

            Assert.AreEqual(0, panel.GetSelectedKeys().Count, "All rows must start unchecked - import must never blindly overwrite.");
        }

        [TestMethod]
        public void HasAnyApplicableGroup_TrueWhenMismatchedOrPackageOnlyPresent()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "distunits" }) });
            Assert.IsTrue(panel.HasAnyApplicableGroup);
        }

        [TestMethod]
        public void HasAnyApplicableGroup_FalseWhenOnlyLiveOnlyGroups()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup> { Group("g1", liveOnly: new[] { "comport" }) });
            Assert.IsFalse(panel.HasAnyApplicableGroup, "LiveOnly-only groups have nothing to apply.");
        }

        [TestMethod]
        public void EmptyGroupList_NoRows_NoSelection()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>());
            Assert.AreEqual(0, panel.GetSelectedKeys().Count);
            Assert.IsFalse(panel.HasAnyApplicableGroup);
        }

        [TestMethod]
        public void SelectAll_ReturnsAllApplicableKeys_AcrossGroups_ExcludingLiveOnly()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "guided_alt", "guided_alt_frame" }),
                Group("g2", packageOnly: new[] { "speechenable" }, liveOnly: new[] { "comport" })
            });

            panel.SelectAll();
            var selected = panel.GetSelectedKeys();

            CollectionAssert.Contains(selected, "guided_alt");
            CollectionAssert.Contains(selected, "guided_alt_frame");
            CollectionAssert.Contains(selected, "speechenable");
            CollectionAssert.DoesNotContain(selected, "comport", "LiveOnly keys have no package value - must never appear as selectable/applicable.");
            Assert.AreEqual(3, selected.Count);
        }

        [TestMethod]
        public void CheckingOneGroup_OnlyThatGroupsKeysAreSelected()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "guided_alt", "guided_alt_frame" }),
                Group("g2", mismatched: new[] { "distunits" })
            });

            panel.SetGroupChecked(0, true);
            var selected = panel.GetSelectedKeys();

            CollectionAssert.Contains(selected, "guided_alt");
            CollectionAssert.Contains(selected, "guided_alt_frame");
            CollectionAssert.DoesNotContain(selected, "distunits");
            Assert.AreEqual(2, selected.Count, "Checking one group must select the whole coupled group, never a subset of it.");
        }

        [TestMethod]
        public void SelectAllThenSelectNone_ClearsSelection()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "distunits" }) });

            panel.SelectAll();
            Assert.AreEqual(1, panel.GetSelectedKeys().Count);

            panel.SelectNone();
            Assert.AreEqual(0, panel.GetSelectedKeys().Count);
        }

        [TestMethod]
        public void SelectAll_RespectsSearchFilter_HiddenGroupsNotChecked()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "distunits" }),
                Group("g2", mismatched: new[] { "speechenable" })
            });

            panel.SearchFilter = "dist";
            panel.SelectAll();

            panel.SearchFilter = ""; // clear filter to inspect the full selection
            var selected = panel.GetSelectedKeys();

            CollectionAssert.Contains(selected, "distunits");
            CollectionAssert.DoesNotContain(selected, "speechenable",
                "SelectAll while filtered by search must not check groups hidden by the filter.");
            Assert.AreEqual(1, selected.Count);
        }

        [TestMethod]
        public void SelectAll_RespectsStatusFilter_HiddenApplicableGroupNotChecked()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "distunits" }),
                Group("g2", liveOnly: new[] { "comport" })
            });

            panel.StatusFilter = 3; // Info only - hides the applicable g1 row
            panel.SelectAll();

            panel.StatusFilter = 0; // All
            Assert.AreEqual(0, panel.GetSelectedKeys().Count,
                "SelectAll while filtered to Info-only rows must not check the hidden applicable group.");
        }

        [TestMethod]
        public void HasAnyApplicableGroup_UnaffectedByActiveFilter()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup> { Group("g1", mismatched: new[] { "distunits" }) });

            panel.StatusFilter = 3; // Info only - hides the one applicable group from the view
            Assert.IsTrue(panel.HasAnyApplicableGroup,
                "HasAnyApplicableGroup must reflect the full group set, not the current filtered view.");
        }

        [TestMethod]
        public void SetGroupChecked_WorksByOriginalIndex_EvenWhenFilteredOutOfView()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "distunits" }),
                Group("g2", mismatched: new[] { "speechenable" })
            });

            panel.SearchFilter = "speech"; // hides g1 (index 0) from the current view
            panel.SetGroupChecked(0, true);

            panel.SearchFilter = "";
            CollectionAssert.Contains(panel.GetSelectedKeys(), "distunits",
                "SetGroupChecked indexes the original Populate() order, independent of any active filter.");
        }

        [TestMethod]
        public void MixedGroup_LiveOnlyKeyNeverSelected_EvenWhenGroupChecked()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "distunits" }, liveOnly: new[] { "speedunits" })
            });

            panel.SetGroupChecked(0, true);
            var selected = panel.GetSelectedKeys();

            CollectionAssert.Contains(selected, "distunits");
            CollectionAssert.DoesNotContain(selected, "speedunits",
                "A LiveOnly key within an otherwise-applicable group has no package value and must never be selected, even though it now gets its own display row.");
            Assert.AreEqual(1, selected.Count);
        }

        [TestMethod]
        public void SelectAll_ChecksWholeCoupledGroup_EvenWhenOnlyOneOfItsRowsIsVisible()
        {
            var panel = new ImportDiffPanel();
            Populate(panel, new List<ConfigDiffGroup>
            {
                Group("g1", mismatched: new[] { "guided_alt", "guided_alt_frame" })
            });

            panel.SearchFilter = "guided_alt_frame"; // matches only one of the two coupled keys
            panel.SelectAll();

            panel.SearchFilter = "";
            var selected = panel.GetSelectedKeys();

            CollectionAssert.Contains(selected, "guided_alt");
            CollectionAssert.Contains(selected, "guided_alt_frame");
            Assert.AreEqual(2, selected.Count,
                "SelectAll must check the WHOLE group once any one of its rows is visible - a coupled pair must never end up half-selected.");
        }
    }
}
