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
        string _presetsFile;
        readonly List<string> _messages = new List<string>();

        [TestInitialize]
        public void Setup()
        {
            _originalConfigFile = WarningEngine.warningconfigfile;
            _originalWarnings = WarningEngine.warnings;
            _tempFile = Path.Combine(Path.GetTempPath(), "AircraftAlertsFormTests_" + Guid.NewGuid().ToString("N") + ".xml");
            _presetsFile = Path.Combine(Path.GetTempPath(), "AircraftAlertsFormTests_" + Guid.NewGuid().ToString("N") + ".json");
            WarningEngine.warningconfigfile = _tempFile;
        }

        [TestCleanup]
        public void Cleanup()
        {
            WarningEngine.warningconfigfile = _originalConfigFile;
            WarningEngine.warnings = _originalWarnings;
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
            if (File.Exists(_presetsFile)) File.Delete(_presetsFile);
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
        public void AddAlert_StartsAsAnArmedAlarmThatDoesNothingYet()
        {
            using (var form = new AircraftAlertsForm(new CustomWarning[0]))
            {
                var alert = form.AddAlert();

                Assert.AreEqual(AlertKind.Alarm, alert.Alert.Kind);
                Assert.AreEqual(AlertPhase.Armed, alert.Alert.Phase);
                Assert.IsTrue(AircraftAlertsForm.Validate(form.Alerts).Any(p => p.Contains("does nothing")));
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
        public void StatusChips_SayWhetherTheAircraftIsSendingData()
        {
            using (var form = new AircraftAlertsForm(new CustomWarning[0]))
            {
                Assert.AreEqual("Aircraft not connected", form.LinkStatus);
                Assert.AreEqual("Speech is off", form.SpeechStatus);

                form.UpdateStatusChips(AircraftLinkStatus.NoData, false);
                Assert.AreEqual("Aircraft connected: no data received", form.LinkStatus);
                Assert.AreEqual("Speech is off", form.SpeechStatus);

                form.UpdateStatusChips(AircraftLinkStatus.Receiving, true);
                Assert.AreEqual("Aircraft connected: receiving data", form.LinkStatus);
                Assert.AreEqual("Speech is on", form.SpeechStatus);

                Assert.IsFalse(Descendants(form).OfType<Label>().Any(l => l.Text.Contains("Judicar values")));
                Assert.IsFalse(Descendants(form).OfType<Label>().Any(l => l.Text.Contains("Flight phase")));
            }
        }

        [TestMethod]
        public void Classify_UsesTheLinkAndHowRecentTheLastHeartbeatWas()
        {
            var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

            Assert.AreEqual(AircraftLinkStatus.NotConnected, AircraftAlertsForm.Classify(false, now, now));
            Assert.AreEqual(AircraftLinkStatus.NoData, AircraftAlertsForm.Classify(true, null, now));
            Assert.AreEqual(AircraftLinkStatus.NoData, AircraftAlertsForm.Classify(true, now.AddSeconds(-4), now));
            Assert.AreEqual(AircraftLinkStatus.Receiving, AircraftAlertsForm.Classify(true, now.AddSeconds(-1), now));
        }

        [TestMethod]
        public void Validate_AcceptsAnAlertThatOnlyColoursTheQuickView()
        {
            var colourOnly = ColourOnly();

            Assert.AreEqual(0, AircraftAlertsForm.Validate(new[] { colourOnly }).Count);
            Assert.IsTrue(AircraftAlertsForm.DoesSomething(colourOnly));
        }

        [TestMethod]
        public void Validate_RejectsAnAlertThatDoesNothing()
        {
            var silent = ColourOnly();
            silent.Alert.Show = false;
            var noColour = ColourOnly();
            noColour.color = AlertEvaluator.NoColour;

            Assert.IsTrue(AircraftAlertsForm.Validate(new[] { silent }).Single().Contains("does nothing"));
            Assert.IsFalse(AircraftAlertsForm.DoesSomething(noColour));

            silent.Alert.Preflight = AlertPreflight.Warning;
            Assert.AreEqual(0, AircraftAlertsForm.Validate(new[] { silent }).Count, "a pre-flight check is enough");
            silent.Alert.Preflight = AlertPreflight.None;
            silent.Alert.StaleText = "Engine RPM lost";
            Assert.AreEqual(0, AircraftAlertsForm.Validate(new[] { silent }).Count, "stale text is enough");
        }

        [TestMethod]
        public void ColourOnlyAlert_ShowsADashUnderSaysAndCannotBeSaidNow()
        {
            using (var form = new AircraftAlertsForm(new[] { ColourOnly() }))
            {
                var row = List(form).Items[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToArray();
                var sayNow = Descendants(form).OfType<Button>().Single(b => b.Text == "Say it now");

                Assert.AreEqual("-", row[4]);
                Assert.IsFalse(sayNow.Enabled);
                Assert.IsTrue(Descendants(form).OfType<Label>().Any(l => l.Text == "Say (optional)"));
            }
        }

        [TestMethod]
        public void ColourOnlyAlert_SurvivesSaveAndReloadWithoutGainingText()
        {
            WarningEngine.warnings = new List<CustomWarning>();

            AircraftAlertsForm.ApplyAlerts(WarningEngine.warnings, new[] { ColourOnly() });
            WarningEngine.SaveConfig();
            WarningEngine.LoadConfig();

            Assert.AreEqual("", WarningEngine.warnings.Single().Text);
        }

        [TestMethod]
        public void SaveAsPreset_WritesTheSelectedAlertToThePresetsFile()
        {
            using (var form = PresetForm(ColourOnly()))
            {
                Assert.IsTrue(form.SaveSelectedAsPreset());

                var saved = AlertPresetStore.Load(_presetsFile);
                Assert.IsNull(saved.Error);
                Assert.AreEqual("RPM colour", saved.Presets.Single().Alert.Title);
                Assert.AreEqual("", saved.Presets.Single().Text);
                CollectionAssert.Contains(form.PresetMenuText(), "RPM colour");
                CollectionAssert.Contains(form.PresetMenuText(), "  RPM colour");
            }
        }

        [TestMethod]
        public void SaveAsPreset_ReplacesASavedPresetOnlyWhenConfirmed()
        {
            using (var form = PresetForm(ColourOnly()))
            {
                form.SaveSelectedAsPreset();
                form.Selected.Warning = 500;
                var asked = 0;

                form.ConfirmReplace = title => { asked++; return false; };
                Assert.IsFalse(form.SaveSelectedAsPreset());
                Assert.AreEqual(1000, AlertPresetStore.Load(_presetsFile).Presets.Single().Warning);

                form.ConfirmReplace = title => { asked++; return true; };
                Assert.IsTrue(form.SaveSelectedAsPreset());
                Assert.AreEqual(500, AlertPresetStore.Load(_presetsFile).Presets.Single().Warning);
                Assert.AreEqual(2, asked);
            }
        }

        [TestMethod]
        public void SaveAsPreset_RefusesABuiltInNameAndAnInvalidAlert()
        {
            using (var form = PresetForm(AlertPresets.EngineOut()))
            {
                Assert.IsFalse(form.SaveSelectedAsPreset());
                Assert.IsTrue(_messages.Single().Contains("built-in preset"));

                form.Selected.Alert.Title = "My engine";
                form.Selected.Text = "";
                form.Selected.Alert.StaleText = "";
                form.Selected.Alert.Show = false;
                Assert.IsFalse(form.SaveSelectedAsPreset());
                Assert.IsTrue(_messages.Last().Contains("does nothing"));
                Assert.IsFalse(File.Exists(_presetsFile));
            }
        }

        [TestMethod]
        public void SaveAsPreset_LeavesAnUnreadablePresetsFileAlone()
        {
            File.WriteAllText(_presetsFile, "{ not json");
            using (var form = PresetForm(ColourOnly()))
            {
                Assert.IsFalse(form.SaveSelectedAsPreset());
                Assert.AreEqual("{ not json", File.ReadAllText(_presetsFile));
                CollectionAssert.Contains(form.PresetMenuText(), "Saved presets could not be read");
            }
        }

        [TestMethod]
        public void SavedPreset_IsAddedWithAUniqueIdAndCanBeRemoved()
        {
            using (var form = PresetForm(ColourOnly()))
            {
                form.SaveSelectedAsPreset();

                var added = form.AddSavedPreset("rpm colour");

                Assert.AreEqual("rpm-colour-2", added.Alert.Id);
                Assert.AreSame(added, form.Selected);
                Assert.AreEqual(2, form.Alerts.Count);

                form.ConfirmRemove = title => true;
                Assert.IsTrue(form.RemoveSavedPreset("RPM colour"));
                Assert.AreEqual(0, AlertPresetStore.Load(_presetsFile).Presets.Count);
                Assert.IsNull(form.AddSavedPreset("RPM colour"));
                CollectionAssert.AreEqual(AlertPresets.Ids.Select(id => AlertPresets.Create(id).Alert.Title).ToList(),
                    form.PresetMenuText());
            }
        }

        AircraftAlertsForm PresetForm(CustomWarning alert)
        {
            var form = new AircraftAlertsForm(new[] { alert })
            {
                PresetsPath = _presetsFile,
                ShowMessage = text => _messages.Add(text),
                ConfirmReplace = title => false,
                ConfirmRemove = title => false
            };
            _messages.Clear();
            return form;
        }

        static CustomWarning ColourOnly()
        {
            var alert = AlertPresets.EngineOut();
            alert.Text = "";
            alert.Alert.StaleText = "";
            alert.Alert.Id = "rpm-colour";
            alert.Alert.Title = "RPM colour";
            alert.Alert.Show = true;
            alert.color = "Orange";
            return alert;
        }
    }
}
