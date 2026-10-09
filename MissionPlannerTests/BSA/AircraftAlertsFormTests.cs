using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.UI;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AircraftAlertsFormTests
    {
        string _originalConfigFile;
        List<CustomWarning> _originalWarnings;
        string _tempFile;

        [TestInitialize]
        public void Setup()
        {
            _originalConfigFile = WarningEngine.warningconfigfile;
            _originalWarnings = WarningEngine.warnings;
            _tempFile = Path.Combine(Path.GetTempPath(), "AircraftAlertsFormTests_" + Guid.NewGuid().ToString("N") + ".xml");
            WarningEngine.warningconfigfile = _tempFile;
        }

        [TestCleanup]
        public void Cleanup()
        {
            WarningEngine.warningconfigfile = _originalConfigFile;
            WarningEngine.warnings = _originalWarnings;
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        static ListView List(Form form) => Descendants(form).OfType<ListView>().Single();

        static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }

        [TestMethod]
        public void Form_ListsOnlyAlertsAndWorksOnCopies()
        {
            var engine = AlertPresets.EngineOut();
            var classic = new CustomWarning { Warning = 5 };

            using (var form = new AircraftAlertsForm(new[] { classic, engine }))
            {
                Assert.AreEqual(1, form.Alerts.Count);
                Assert.AreNotSame(engine, form.Alerts[0]);
                Assert.AreEqual(1, List(form).Items.Count);
                Assert.IsFalse(form.Dirty);
            }
        }

        [TestMethod]
        public void Form_RowsDescribeEachAlert()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut(), AlertPresets.AirspeedReadout() }))
            {
                var rows = List(form).Items.Cast<ListViewItem>()
                    .Select(i => i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToArray()).ToList();

                CollectionAssert.AreEqual(
                    new[] { "Engine out", "rpm1", "Airborne, including hover", "below 1000 for 1 s", "Engine failure", "10 s", "-" },
                    rows[0]);
                CollectionAssert.AreEqual(
                    new[] { "Airspeed readout", "airspeed", "Transition and forward flight", "always", "{value}", "3 s", "-" },
                    rows[1]);
                Assert.IsTrue(List(form).Items[0].Checked);
                Assert.IsFalse(List(form).Items[1].Checked);
            }
        }

        [TestMethod]
        public void AddFromPreset_AddsSelectsAndMarksDirty()
        {
            using (var form = new AircraftAlertsForm(new CustomWarning[0]))
            {
                var added = form.AddPreset(AlertPresets.GpsRedundancyLostId);
                var again = form.AddPreset(AlertPresets.GpsRedundancyLostId);

                Assert.AreEqual(2, form.Alerts.Count);
                Assert.AreEqual("gps-redundancy-lost", added.Alert.Id);
                Assert.AreEqual("gps-redundancy-lost-2", again.Alert.Id);
                Assert.AreSame(again, form.Selected);
                Assert.IsTrue(form.Dirty);
            }
        }

        [TestMethod]
        public void AddAlert_StartsAsAnArmedAlarmThatStillNeedsText()
        {
            using (var form = new AircraftAlertsForm(new CustomWarning[0]))
            {
                var alert = form.AddAlert();

                Assert.AreEqual(AlertKind.Alarm, alert.Alert.Kind);
                Assert.AreEqual(AlertPhase.Armed, alert.Alert.Phase);
                Assert.IsTrue(AircraftAlertsForm.Validate(form.Alerts).Any(p => p.Contains("say")));
            }
        }

        [TestMethod]
        public void Dirty_FollowsWhatChangedNotHowItChanged()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut() }))
            {
                Assert.IsFalse(form.Dirty);
                Assert.AreEqual("No unsaved changes", form.SaveStatus);
                form.AddPreset(AlertPresets.AirspeedReadout().Alert.Id);
                Assert.IsTrue(form.Dirty);
                Assert.AreEqual("Unsaved changes", form.SaveStatus);
                form.DeleteSelected();
                Assert.IsFalse(form.Dirty, "back to what was loaded");
                Assert.AreEqual("No unsaved changes", form.SaveStatus);
                form.Alerts[0].RepeatTime = 12;
                Assert.IsTrue(form.Dirty);
            }
        }

        [TestMethod]
        public void NumberBoxes_KeepTheThemeColoursAndMarkBadInputReadably()
        {
            var themedBack = System.Drawing.Color.FromArgb(38, 39, 40);
            var themedFore = System.Drawing.Color.White;
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut(), AlertPresets.AirspeedReadout() }))
            {
                var boxes = Descendants(form).OfType<TextBox>().ToList();
                foreach (var box in boxes)
                {
                    box.BackColor = themedBack;
                    box.ForeColor = themedFore;
                }

                form.AddPreset(AlertPresets.GpsRedundancyLostId);
                Assert.IsTrue(boxes.All(b => b.BackColor == themedBack && b.ForeColor == themedFore),
                    "loading an alert must not repaint the boxes white");

                var threshold = boxes.Single(b => b.Text == "1");
                threshold.Text = "one";
                Assert.AreEqual(System.Drawing.Color.MistyRose, threshold.BackColor);
                Assert.AreEqual(System.Drawing.Color.Black, threshold.ForeColor);

                threshold.Text = "2";
                Assert.AreEqual(themedBack, threshold.BackColor);
                Assert.AreEqual(themedFore, threshold.ForeColor);
                Assert.AreEqual(2, form.Selected.Warning);
            }
        }

        [TestMethod]
        public void Delete_RemovesTheSelectedAlert()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut(), AlertPresets.AirspeedReadout() }))
            {
                Assert.AreEqual("engine-out", form.Selected.Alert.Id);

                form.DeleteSelected();

                Assert.AreEqual(1, form.Alerts.Count);
                Assert.AreEqual("airspeed-readout", form.Selected.Alert.Id);
            }
        }

        [TestMethod]
        public void ApplyAlerts_KeepsClassicWarningsAndReplacesAlerts()
        {
            var classic = new CustomWarning { Warning = 5 };
            var shared = new List<CustomWarning> { AlertPresets.EngineOut(), classic, AlertPresets.AirspeedReadout() };

            AircraftAlertsForm.ApplyAlerts(shared, new[] { AlertPresets.Create(AlertPresets.GpsRedundancyLostId) });

            Assert.AreEqual(2, shared.Count);
            Assert.AreSame(classic, shared[0]);
            Assert.AreEqual("gps-redundancy-lost", shared[1].Alert.Id);
        }

        [TestMethod]
        public void SavedFile_CarriesAnAlertElementPerAlert()
        {
            WarningEngine.warnings = new List<CustomWarning> { new CustomWarning { Warning = 5 } };

            AircraftAlertsForm.ApplyAlerts(WarningEngine.warnings, AlertPresets.All());
            WarningEngine.SaveConfig();

            var text = File.ReadAllText(_tempFile);
            Assert.AreEqual(5, text.Split(new[] { "<Alert>" }, StringSplitOptions.None).Length - 1);
            WarningEngine.LoadConfig();
            Assert.AreEqual(6, WarningEngine.warnings.Count);
            Assert.AreEqual(5, WarningEngine.warnings.Count(w => w.Alert != null));
        }

        [TestMethod]
        public void Validate_CatchesWhatWouldMakeAnAlertUseless()
        {
            var noTrigger = AlertPresets.EngineOut();
            noTrigger.ConditionType = CustomWarning.Conditional.NONE;
            var noSource = AlertPresets.EngineOut();
            noSource.Alert.Id = "other";
            noSource.Alert.Source = "rpm1";
            var duplicate = AlertPresets.EngineOut();

            var problems = AircraftAlertsForm.Validate(new[] { noTrigger, noSource, duplicate });

            Assert.IsTrue(problems.Any(p => p.Contains("trigger")));
            Assert.IsTrue(problems.Any(p => p.Contains("watches")));
            Assert.IsTrue(problems.Any(p => p.Contains("unique id")));
        }

        [TestMethod]
        public void ParseWatch_UnderstandsEveryWayOfNamingASource()
        {
            Assert.AreEqual("field:rpm1", AircraftAlertsForm.ParseWatch("rpm1", null));
            Assert.AreEqual("named:ESC_HOT", AircraftAlertsForm.ParseWatch("ESC_HOT (named value)", null));
            Assert.AreEqual("named:FUEL_PCT", AircraftAlertsForm.ParseWatch("fuel_pct", null));
            Assert.AreEqual(AlertSource.NamedFreshCount, AircraftAlertsForm.ParseWatch("Fresh Judicar values", null));
            Assert.AreEqual(AlertSource.GpsFixCount, AircraftAlertsForm.ParseWatch("GPS receivers with a fix", null));
            Assert.AreEqual("field:airspeed", AircraftAlertsForm.ParseWatch("field:airspeed", null));
            Assert.AreEqual("field:rpm1", AircraftAlertsForm.ParseWatch(" ", "field:rpm1"));
        }

        [TestMethod]
        public void PreviewText_GivesSomethingSayable()
        {
            Assert.AreEqual("25", AircraftAlertsForm.PreviewText(AlertPresets.AirspeedReadout()));
            Assert.AreEqual("Engine failure", AircraftAlertsForm.PreviewText(AlertPresets.EngineOut()));
        }

        [TestMethod]
        public void StatusChips_SayWhatTheAlertsCanSee()
        {
            using (var form = new AircraftAlertsForm(new CustomWarning[0]))
            {
                form.UpdateStatusChips(true, false, 3, 12, false);

                Assert.AreEqual("Flight phase: not received (requested while an alert needs it)", form.PhaseStatus);
                Assert.AreEqual("Judicar values: 3 of 12 fresh", form.NamedStatus);
                Assert.AreEqual("Speech is off in Mission Planner: alerts will only show", form.SpeechStatus);

                form.UpdateStatusChips(true, true, 12, 12, true);
                Assert.AreEqual("Flight phase: receiving", form.PhaseStatus);
                Assert.AreEqual("Speech is on", form.SpeechStatus);
            }
        }
    }
}
