using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaBundleProfileCatalogTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>
            {
                new KeyPolicyRule { Match = "distunits", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "quickview*", Class = KeyClass.Portable }
            },
            Default = KeyClass.MachineSpecific
        };

        static Dictionary<string, string> Live() => new Dictionary<string, string>
        {
            ["distunits"] = "0",
            ["quickViewRows"] = "1",
            ["quickViewCols"] = "1",
            ["quickView1"] = "MAV_ESC_HOT",
            ["quickView1_label"] = "ESC"
        };

        static ConfigPackageContents ExportAndRead(string optionId)
        {
            var live = Live();
            var checklist = Path.Combine(Path.GetTempPath(), "BsaProfileCatalog_" + Guid.NewGuid().ToString("N") + ".json");
            var keyPolicy = Path.Combine(Path.GetTempPath(), "BsaProfileCatalog_" + Guid.NewGuid().ToString("N") + ".json");
            var output = Path.Combine(Path.GetTempPath(), "BsaProfileCatalog_" + Guid.NewGuid().ToString("N") + ".bsampconfig");
            File.WriteAllText(checklist, "{}");
            File.WriteAllText(keyPolicy, "{}");
            try
            {
                var option = BsaBundleProfileCatalog.Find(optionId);
                var profile = BsaBundleProfileCatalog.CreateFor(option,
                    () => BsaQuickViewCodec.Export(live, new Dictionary<string, string>()));
                BsaConfigExporter.Export(output, live, Policy(), checklist, keyPolicy, null, null,
                    "1.0.0", "op", "1.3.83", "", profile, option.PackageId);
                return BsaConfigPackage.Read(output);
            }
            finally
            {
                File.Delete(checklist);
                File.Delete(keyPolicy);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [TestMethod]
        public void Options_OfferJudicarAndNoProfile_WithUniqueIds()
        {
            var ids = BsaBundleProfileCatalog.Options.Select(o => o.Id).ToList();

            CollectionAssert.Contains(ids, Judicar2600BundleProfile.PackageId);
            CollectionAssert.Contains(ids, BsaBundleProfileCatalog.NoProfileId);
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            Assert.IsTrue(BsaBundleProfileCatalog.Options.All(o => !string.IsNullOrWhiteSpace(o.DisplayName) && !string.IsNullOrWhiteSpace(o.Description)));
        }

        [TestMethod]
        public void Find_UnknownProfile_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => BsaBundleProfileCatalog.Find("aero.bullshark.unknown"));
            Assert.ThrowsException<ArgumentException>(() => BsaBundleProfileCatalog.Find(null));
        }

        [TestMethod]
        public void Judicar_ExportsASchema2BundleStampedJudicar2600()
        {
            var read = ExportAndRead(Judicar2600BundleProfile.PackageId);

            Assert.AreEqual((int?)2, read.Manifest.SchemaVersion);
            Assert.AreEqual(Judicar2600BundleProfile.PackageId, read.Manifest.PackageId);
            Assert.IsTrue(read.HasCompleteCoreProfile);
            Assert.IsFalse(read.ConfigSubset.ContainsKey("quickView1_label"));
        }

        [TestMethod]
        public void NoProfile_ExportsASettingsPackage_WithTheQuickPanelAndNoJudicarStamp()
        {
            var read = ExportAndRead(BsaBundleProfileCatalog.NoProfileId);

            Assert.IsTrue(read.IsLegacy);
            Assert.IsNull(read.QuickView);
            Assert.IsNull(read.HealthRules);
            Assert.AreNotEqual(Judicar2600BundleProfile.PackageId, read.Manifest.PackageId);
            Assert.AreEqual("ESC", read.ConfigSubset["quickView1_label"]);
            Assert.AreEqual("0", read.ConfigSubset["distunits"]);
        }

        [TestMethod]
        public void NoProfile_NeverReadsTheQuickPanelProfile()
        {
            var profile = BsaBundleProfileCatalog.CreateFor(BsaBundleProfileCatalog.Find(BsaBundleProfileCatalog.NoProfileId),
                () => throw new InvalidOperationException("QuickView source 'customfield9' has no stable MAV_* identity."));

            Assert.IsNull(profile);
        }

        [TestMethod]
        public void ChoiceForm_StartsWithNothingChosen_AndCannotContinue()
        {
            using (var form = new BundleProfileChoiceForm(BsaBundleProfileCatalog.Options))
            {
                Assert.IsNull(form.SelectedOptionId);
                Assert.IsFalse(form.CanContinue);
            }
        }

        [TestMethod]
        public void ChoiceForm_ChoosingAProfile_EnablesContinueAndReportsIt()
        {
            using (var form = new BundleProfileChoiceForm(BsaBundleProfileCatalog.Options))
            {
                form.Choose(BsaBundleProfileCatalog.NoProfileId);
                Assert.IsTrue(form.CanContinue);
                Assert.AreEqual(BsaBundleProfileCatalog.NoProfileId, form.SelectedOptionId);

                form.Choose(Judicar2600BundleProfile.PackageId);
                Assert.AreEqual(Judicar2600BundleProfile.PackageId, form.SelectedOptionId);
            }
        }
    }
}
