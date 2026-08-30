using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class Judicar2600LegacyMigrationTests
    {
        [TestMethod]
        public void CreateProfile_MapsReviewedCustomFieldsToStableIds()
        {
            var settings = new Dictionary<string, string>
            {
                ["quickViewRows"] = "1", ["quickViewCols"] = "2",
                ["quickView1"] = "customfield0", ["quickView2"] = "customfield12"
            };
            var legacy = new ConfigPackageContents { IsLegacy = true, ConfigSubset = settings };

            var profile = Judicar2600LegacyMigration.CreateProfile(legacy);

            Assert.AreEqual("MAV_VTOL_RES", profile.QuickView.Cells[0].SourceId);
            Assert.IsNull(profile.QuickView.Cells[1].SourceId);
            Assert.IsFalse(profile.QuickView.Cells[1].Visible);
        }
    }
}
