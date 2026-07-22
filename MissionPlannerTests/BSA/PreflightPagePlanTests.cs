using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class PreflightPagePlanTests
    {
        static PreflightCheckDefinition Manual(string id, string group = null) => new PreflightCheckDefinition
        {
            Id = id,
            Title = id,
            Type = CheckType.Manual,
            Severity = CheckSeverity.Critical,
            Instruction = "do it",
            Group = group
        };

        static PreflightCheckDefinition Semi(string id, string group = null) => new PreflightCheckDefinition
        {
            Id = id,
            Title = id,
            Type = CheckType.Semi,
            Severity = CheckSeverity.Warning,
            Instruction = "confirm it",
            Source = CheckSource.Telemetry,
            Field = "x",
            Condition = CheckCondition.GTEQ,
            Value = 1,
            Group = group
        };

        static PreflightCheckDefinition Auto(string id, string group = null) => new PreflightCheckDefinition
        {
            Id = id,
            Title = id,
            Type = CheckType.Auto,
            Severity = CheckSeverity.Warning,
            Source = CheckSource.Telemetry,
            Field = "x",
            Condition = CheckCondition.GTEQ,
            Value = 1,
            Group = group
        };

        [TestMethod]
        public void GroupingAndOrder_MatchesDeclaredGroupsAndAuthoredOrder()
        {
            var checks = new List<PreflightCheckDefinition>
            {
                Manual("b1", "B"),
                Manual("a2", "A"),
                Manual("a1", "A"),
                Manual("b2", "B")
            };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A", "B" }, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.AreEqual(2, pages.Count);
            Assert.AreEqual("A", pages[0].GroupName);
            CollectionAssert.AreEqual(new[] { "a2", "a1" }, pages[0].Checks.Select(c => c.Id).ToList());
            Assert.AreEqual("B", pages[1].GroupName);
            CollectionAssert.AreEqual(new[] { "b1", "b2" }, pages[1].Checks.Select(c => c.Id).ToList());
        }

        [TestMethod]
        public void NoDeclaredGroups_OneImplicitGroup()
        {
            var checks = new List<PreflightCheckDefinition> { Manual("c1"), Manual("c2") };
            var pages = PreflightPagePlan.Build(checks, new PreflightChecklistMetadata { AutoChecksFirst = false });

            Assert.AreEqual(1, pages.Count);
            Assert.AreEqual(PreflightPagePlan.ImplicitGroupName, pages[0].GroupName);
        }

        [TestMethod]
        public void AutoHoist_EveryAutoOnLeadingPage_SemiNeverHoisted()
        {
            var checks = new List<PreflightCheckDefinition>
            {
                Manual("m1", "A"),
                Auto("auto1", "A"),
                Semi("semi1", "A"),
                Auto("auto2", "A")
            };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, AutoChecksFirst = true };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.IsTrue(pages[0].IsAutoPage);
            Assert.AreEqual("System checks", pages[0].GroupName);
            CollectionAssert.AreEqual(new[] { "auto1", "auto2" }, pages[0].Checks.Select(c => c.Id).ToList());

            var groupAChecks = pages.Where(p => !p.IsAutoPage).SelectMany(p => p.Checks).Select(c => c.Id).ToList();
            CollectionAssert.AreEqual(new[] { "m1", "semi1" }, groupAChecks);
        }

        [TestMethod]
        public void AutoHoist_Disabled_AutosStayInTheirGroup()
        {
            var checks = new List<PreflightCheckDefinition> { Manual("m1", "A"), Auto("auto1", "A") };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.AreEqual(1, pages.Count);
            Assert.IsFalse(pages[0].IsAutoPage);
            CollectionAssert.AreEqual(new[] { "m1", "auto1" }, pages[0].Checks.Select(c => c.Id).ToList());
        }

        [TestMethod]
        public void NoAutoChecks_NoAutoPageEmitted()
        {
            var checks = new List<PreflightCheckDefinition> { Manual("m1", "A") };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, AutoChecksFirst = true };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.IsFalse(pages.Any(p => p.IsAutoPage));
        }

        [TestMethod]
        public void BalancedPagination_ThirteenAtFive_Is_5_4_4()
        {
            var checks = Enumerable.Range(1, 13).Select(i => Manual("c" + i, "A")).ToList();
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, PageSize = 5, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            CollectionAssert.AreEqual(new[] { 5, 4, 4 }, pages.Select(p => p.Checks.Count).ToList());
            Assert.IsTrue(pages.All(p => p.PagesInGroup == 3));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pages.Select(p => p.PageInGroup).ToList());
        }

        [TestMethod]
        public void BalancedPagination_ElevenAtFive_Is_4_4_3()
        {
            var checks = Enumerable.Range(1, 11).Select(i => Manual("c" + i, "A")).ToList();
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, PageSize = 5, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            CollectionAssert.AreEqual(new[] { 4, 4, 3 }, pages.Select(p => p.Checks.Count).ToList());
        }

        [TestMethod]
        public void BalancedPagination_NeverProducesAOneItemOrphanPage()
        {
            // Greedy chunking of 11 @ 5 would produce 5,5,1 - the exact orphan this guards against.
            var checks = Enumerable.Range(1, 11).Select(i => Manual("c" + i, "A")).ToList();
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A" }, PageSize = 5, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.IsFalse(pages.Any(p => p.Checks.Count == 1));
        }

        [TestMethod]
        public void DeclaredGroupWithZeroChecks_SkippedNotError()
        {
            var checks = new List<PreflightCheckDefinition> { Manual("a1", "A") };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A", "Empty" }, AutoChecksFirst = false };

            var pages = PreflightPagePlan.Build(checks, metadata);

            Assert.AreEqual(1, pages.Count);
            Assert.IsFalse(pages.Any(p => p.GroupName == "Empty"));
        }

        [TestMethod]
        public void Build_NeverMutatesOrReordersTheInputChecks()
        {
            var checks = new List<PreflightCheckDefinition>
            {
                Auto("auto1", "A"),
                Manual("b1", "B"),
                Manual("a1", "A")
            };
            var metadata = new PreflightChecklistMetadata { Groups = new List<string> { "A", "B" }, AutoChecksFirst = true };

            var hashBefore = BsaHash.HashObject(checks);
            PreflightPagePlan.Build(checks, metadata);
            var hashAfter = BsaHash.HashObject(checks);

            Assert.AreEqual(hashBefore, hashAfter);
            CollectionAssert.AreEqual(new[] { "auto1", "b1", "a1" }, checks.Select(c => c.Id).ToList());
        }
    }
}
