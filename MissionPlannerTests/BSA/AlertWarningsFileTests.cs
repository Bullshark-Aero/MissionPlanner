using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertWarningsFileTests
    {
        string _originalConfigFile;
        List<CustomWarning> _originalWarnings;
        object _originalSource;
        string _tempFile;

        [TestInitialize]
        public void Setup()
        {
            _originalConfigFile = WarningEngine.warningconfigfile;
            _originalWarnings = WarningEngine.warnings;
            _originalSource = CustomWarning.defaultsrc;
            _tempFile = Path.Combine(Path.GetTempPath(), "AlertWarningsFileTests_" + Guid.NewGuid().ToString("N") + ".xml");
            WarningEngine.warningconfigfile = _tempFile;
        }

        [TestCleanup]
        public void Cleanup()
        {
            WarningEngine.Stop();
            WarningEngine.warningconfigfile = _originalConfigFile;
            WarningEngine.warnings = _originalWarnings;
            CustomWarning.defaultsrc = _originalSource;
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        static CustomWarning EscAlert() => new CustomWarning
        {
            Text = "ESC telemetry lost",
            RepeatTime = 10,
            color = "Red",
            Alert = new AlertOptions
            {
                Id = "esc-telemetry-lost",
                Title = "ESC telemetry lost",
                Kind = AlertKind.Alarm,
                Source = "named:ESC_HOT",
                FreshSeconds = 5,
                ArmedGraceSeconds = 5,
                Phase = AlertPhase.Armed,
                WhenPhaseUnknown = AlertPhaseUnknown.StayQuiet,
                TriggerOnStale = true,
                StaleText = "lost",
                Show = true,
                Preflight = AlertPreflight.Warning,
                SourceNames = new List<string> { "ESC_HOT", "FUEL" }
            }
        };

        [TestMethod]
        public void Alert_RoundTripsThroughWarningsXml()
        {
            CustomWarning.defaultsrc = new CurrentState();
            WarningEngine.warnings = new List<CustomWarning> { new CustomWarning { Name = "alt", Warning = 50 }, EscAlert() };
            WarningEngine.SaveConfig();
            WarningEngine.warnings = new List<CustomWarning>();

            WarningEngine.LoadConfig();

            Assert.AreEqual(2, WarningEngine.warnings.Count);
            Assert.IsNull(WarningEngine.warnings[0].Alert);
            var loaded = WarningEngine.warnings[1];
            Assert.AreEqual("", loaded.Name);
            Assert.AreEqual("ESC telemetry lost", loaded.Text);
            Assert.AreEqual("Red", loaded.color);
            var alert = loaded.Alert;
            Assert.AreEqual("esc-telemetry-lost", alert.Id);
            Assert.AreEqual(AlertKind.Alarm, alert.Kind);
            Assert.AreEqual("named:ESC_HOT", alert.Source);
            Assert.AreEqual(5, alert.FreshSeconds);
            Assert.AreEqual(5, alert.ArmedGraceSeconds);
            Assert.AreEqual(AlertPhase.Armed, alert.Phase);
            Assert.AreEqual(AlertPhaseUnknown.StayQuiet, alert.WhenPhaseUnknown);
            Assert.IsTrue(alert.TriggerOnStale);
            Assert.IsTrue(alert.Show);
            Assert.AreEqual(AlertPreflight.Warning, alert.Preflight);
            CollectionAssert.AreEqual(new[] { "ESC_HOT", "FUEL" }, alert.SourceNames);
        }

        [TestMethod]
        public void ClassicWarnings_AreWrittenWithoutAnAlertElement()
        {
            WarningEngine.warnings = new List<CustomWarning> { new CustomWarning { Warning = 50 } };
            WarningEngine.SaveConfig();

            Assert.IsFalse(File.ReadAllText(_tempFile).Contains("<Alert"));
        }

        [TestMethod]
        public void LegacyFile_LoadsUnchanged()
        {
            File.WriteAllText(_tempFile,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<ArrayOfCustomWarning xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\r\n" +
                "  <CustomWarning>\r\n" +
                "    <Name>alt</Name>\r\n" +
                "    <Warning>50</Warning>\r\n" +
                "    <RepeatTime>10</RepeatTime>\r\n" +
                "    <ConditionType>GT</ConditionType>\r\n" +
                "    <Text>WARNING: {name} is {value}</Text>\r\n" +
                "  </CustomWarning>\r\n" +
                "</ArrayOfCustomWarning>\r\n");
            CustomWarning.defaultsrc = new CurrentState();

            WarningEngine.LoadConfig();

            Assert.AreEqual(1, WarningEngine.warnings.Count);
            Assert.AreEqual("alt", WarningEngine.warnings[0].Name);
            Assert.AreEqual(CustomWarning.Conditional.GT, WarningEngine.warnings[0].ConditionType);
            Assert.IsNull(WarningEngine.warnings[0].Alert);
        }

        [TestMethod]
        public void WarningEngine_SkipsAlertsAndStillEvaluatesLaterWarnings()
        {
            CustomWarning.defaultsrc = new CurrentState();
            var classic = new CustomWarning
            {
                Name = "alt",
                ConditionType = CustomWarning.Conditional.GTEQ,
                Warning = -100000,
                Text = "classic fired"
            };
            WarningEngine.warnings = new List<CustomWarning> { EscAlert(), classic };
            var spoken = new List<string>();
            EventHandler<string> handler = (sender, text) => spoken.Add(text);
            WarningEngine.WarningMessage += handler;
            try
            {
                WarningEngine.Start(null);
            }
            finally
            {
                WarningEngine.Stop();
                WarningEngine.WarningMessage -= handler;
            }

            CollectionAssert.Contains(spoken, "classic fired");
            Assert.IsFalse(spoken.Contains("ESC telemetry lost"));
        }

        [TestMethod]
        public void WarningsManager_ListsClassicWarningsOnly()
        {
            WarningEngine.warnings = new List<CustomWarning> { EscAlert(), EscAlert() };

            using (var form = new WarningsManager())
            {
                var panel = form.Controls.Find("panel1", true).Single();

                Assert.AreEqual(0, panel.Controls.OfType<WarningControl>().Count());
            }
            Assert.AreEqual(2, WarningEngine.warnings.Count, "opening the old screen must not drop alerts");
        }
    }
}
