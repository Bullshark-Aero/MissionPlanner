using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaWarningProfileAdapterTests
    {
        static BsaWarningProfile Profile() => Judicar2600BundleProfile.Create(new BsaQuickViewProfile
        {
            Rows = 1, Columns = 1, Cells = { new BsaQuickViewCell { Position = 1 } }
        }).Warnings;

        [TestMethod]
        public void Merge_PreservesUnrelatedAndReimportIsIdempotent()
        {
            var unrelated = new CustomWarning { Name = "battery_remaining", Warning = 20, Text = "Battery low" };
            var first = BsaWarningProfileAdapter.Merge(new[] { unrelated }, Profile(), null);

            Assert.AreEqual(4, first.Warnings.Count);
            Assert.AreSame(unrelated, first.Warnings[0]);
            Assert.IsTrue(first.Warnings.Skip(1).All(w => w.Name.StartsWith("J26_")));

            var second = BsaWarningProfileAdapter.Merge(first.Warnings, Profile(), first.Ownership);
            Assert.AreEqual(4, second.Warnings.Count);
            Assert.AreEqual(0, second.Conflicts.Count);
        }

        [TestMethod]
        public void Merge_EditedOwnedWarningSurfacesConflict()
        {
            var first = BsaWarningProfileAdapter.Merge(new List<CustomWarning>(), Profile(), null);
            first.Warnings[0].Warning = 0.25;
            var second = BsaWarningProfileAdapter.Merge(first.Warnings, Profile(), first.Ownership);
            Assert.AreEqual(1, second.Conflicts.Count);
        }
    }
}
