using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class HealthRuleMigrationTests
    {
        static BsaHealthRuleSet Rules() => Judicar2600BundleProfile.Create(new BsaQuickViewProfile()).HealthRules;

        static CustomWarning Legacy(string field) => new CustomWarning
        {
            Name = field,
            ConditionType = CustomWarning.Conditional.EQ,
            Warning = 0,
            Text = field + " lost"
        };

        static ConfigPackageContents Bundle(string warningsXml, BsaHealthRuleSet rules) => new ConfigPackageContents
        {
            Manifest = new PackageManifest { SchemaVersion = 2, PackageId = Judicar2600BundleProfile.PackageId },
            WarningsXml = warningsXml,
            HealthRules = rules
        };

        [TestMethod]
        public void Migrate_ReplacesSpokenHealthWarningsWithTheThreeAlerts()
        {
            var classic = new CustomWarning { Name = "alt", Warning = 100, Text = "high" };
            var colouring = new CustomWarning { Name = "J26_ESC_OK", type = CustomWarning.WarningType.Coloring, color = "Red" };
            var chained = new CustomWarning { Name = "armed", Child = Legacy("J26_GPS_RED_OK") };

            var migrated = HealthRuleMigration.Migrate(
                new[] { Legacy("J26_DATA_OK"), classic, Legacy("J26_ESC_OK"), colouring, chained }, Rules());

            CollectionAssert.AreEqual(
                new[] { "alt", "J26_ESC_OK", "", "", "" },
                migrated.Select(w => w.Name).ToArray());
            Assert.AreSame(classic, migrated[0]);
            Assert.AreSame(colouring, migrated[1], "a colouring warning on a health field never speaks, so it stays");
            CollectionAssert.AreEqual(AlertPresets.HealthIds, migrated.Skip(2).Select(w => w.Alert.Id).ToArray());
        }

        [TestMethod]
        public void Migrate_TakesTimingAndInputsFromTheRules()
        {
            var rules = Rules();
            rules.Rules[0].FreshnessSeconds = 7;
            rules.Rules[0].ArmedGraceSeconds = 3;
            rules.Rules[0].InputFieldIds = new List<string> { "MAV_A", "B" };
            rules.Rules[1].InputFieldIds = new List<string> { "MAV_ESC_TEMP" };

            var migrated = HealthRuleMigration.Migrate(new CustomWarning[0], rules);

            var dataLost = migrated.Single(w => w.Alert.Id == AlertPresets.AircraftDataLostId).Alert;
            Assert.AreEqual(7, dataLost.FreshSeconds);
            Assert.AreEqual(3, dataLost.ArmedGraceSeconds);
            CollectionAssert.AreEqual(new[] { "A", "B" }, dataLost.SourceNames);
            Assert.AreEqual("named:ESC_TEMP", migrated.Single(w => w.Alert.Id == AlertPresets.EscTelemetryLostId).Alert.Source);
        }

        [TestMethod]
        public void Migrate_NeverDuplicatesAnAlertAlreadyThere()
        {
            var edited = AlertPresets.Create(AlertPresets.GpsRedundancyLostId);
            edited.Text = "Second GPS gone";

            var once = HealthRuleMigration.Migrate(new[] { edited }, Rules());
            var twice = HealthRuleMigration.Migrate(once, Rules());

            Assert.AreEqual(3, twice.Count);
            Assert.AreEqual("Second GPS gone", twice.Single(w => w.Alert.Id == AlertPresets.GpsRedundancyLostId).Text);
        }

        [TestMethod]
        public void Migrate_WithoutRulesOnlyDropsLegacySpeech()
        {
            var migrated = HealthRuleMigration.Migrate(new[] { Legacy("J26_DATA_OK"), AlertPresets.EngineOut() }, null);

            Assert.AreEqual(AlertPresets.EngineOutId, migrated.Single().Alert.Id);
        }

        [TestMethod]
        public void WarningsToInstall_FollowsTheBundle()
        {
            var bundleXml = HealthRuleMigration.Serialize(new[] { Legacy("J26_DATA_OK"), AlertPresets.EngineOut() });
            var machineXml = HealthRuleMigration.Serialize(new[] { new CustomWarning { Name = "alt", Text = "x" } });

            Assert.IsNull(HealthRuleMigration.WarningsToInstall(Bundle(bundleXml, Rules()), false, machineXml));
            Assert.AreEqual(bundleXml, HealthRuleMigration.WarningsToInstall(Bundle(bundleXml, null), true, machineXml));

            var fromBundle = HealthRuleMigration.Parse(HealthRuleMigration.WarningsToInstall(Bundle(bundleXml, Rules()), true, machineXml));
            CollectionAssert.AreEqual(
                new[] { AlertPresets.EngineOutId }.Concat(AlertPresets.HealthIds).ToArray(),
                fromBundle.Select(w => w.Alert?.Id).ToArray());

            var fromMachine = HealthRuleMigration.Parse(HealthRuleMigration.WarningsToInstall(Bundle(null, Rules()), true, machineXml));
            Assert.AreEqual("alt", fromMachine[0].Name);
            Assert.AreEqual(4, fromMachine.Count);

            var unreadable = HealthRuleMigration.Parse(HealthRuleMigration.WarningsToInstall(Bundle(null, Rules()), true, "not xml"));
            Assert.AreEqual(3, unreadable.Count);
        }

        [TestMethod]
        public void WouldChange_ComparesWhatTheFilesMean()
        {
            var xml = HealthRuleMigration.Serialize(new[] { AlertPresets.EngineOut() });

            Assert.IsFalse(HealthRuleMigration.WouldChange(null, xml));
            Assert.IsTrue(HealthRuleMigration.WouldChange(xml, null));
            Assert.IsFalse(HealthRuleMigration.WouldChange(xml, xml.Replace("\r\n", "\n")));
            Assert.IsTrue(HealthRuleMigration.WouldChange(xml, HealthRuleMigration.Serialize(new CustomWarning[0])));
            Assert.IsTrue(HealthRuleMigration.WouldChange(xml, "garbage"));
        }

        [TestMethod]
        public void Summary_CountsAlertsAndWarnings()
        {
            var xml = HealthRuleMigration.Serialize(new[] { AlertPresets.EngineOut(), new CustomWarning { Name = "alt", Text = "x" } });

            Assert.AreEqual("\n\nAlerts and warnings: replaced with the bundle's 1 alert and 1 warning.",
                HealthRuleMigration.Summary(xml, true));
            StringAssert.Contains(HealthRuleMigration.Summary(xml, false), "this machine's are kept");
            Assert.AreEqual("", HealthRuleMigration.Summary(null, true));
        }

        [TestMethod]
        public void ImportWizard_InstallsAlertsAutomaticallyOnlyForAircraftBundles()
        {
            Assert.IsTrue(ImportWizardForm.InstallsWarningsAutomatically(Bundle(null, Rules())));
            Assert.IsTrue(ImportWizardForm.InstallsWarningsAutomatically(Bundle("<ArrayOfCustomWarning />", null)));
            Assert.IsFalse(ImportWizardForm.InstallsWarningsAutomatically(Bundle(null, null)));
            var settingsPackage = new ConfigPackageContents
            {
                Manifest = new PackageManifest { SchemaVersion = 1 },
                IsLegacy = true,
                WarningsXml = "<ArrayOfCustomWarning />"
            };
            Assert.IsFalse(ImportWizardForm.InstallsWarningsAutomatically(settingsPackage));
        }
    }
}
