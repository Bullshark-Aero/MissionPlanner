using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertPresetStoreTests
    {
        string _directory;
        string _path;

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "AlertPresetStoreTests_" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_directory, "config", "alert_presets.json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [TestMethod]
        public void SaveAndLoad_RoundTripsEveryBuiltInPreset()
        {
            AlertPresetStore.Save(_path, AlertPresets.All());

            var loaded = AlertPresetStore.Load(_path);

            Assert.IsNull(loaded.Error);
            Assert.AreEqual(AlertPresetStore.Serialize(AlertPresets.All()), AlertPresetStore.Serialize(loaded.Presets));
            var data = loaded.Presets.Single(p => p.Alert.Id == AlertPresets.AircraftDataLostId);
            CollectionAssert.AreEqual(AlertPresets.Create(AlertPresets.AircraftDataLostId).Alert.SourceNames, data.Alert.SourceNames);
            Assert.IsFalse(loaded.Presets.Single(p => p.Alert.Id == AlertPresets.AirspeedReadoutId).Alert.Enabled);
            Assert.IsFalse(File.Exists(_path + ".tmp"));
        }

        [TestMethod]
        public void Serialize_WritesEnumsAsNames()
        {
            var json = AlertPresetStore.Serialize(new[] { AlertPresets.EngineOut() });

            StringAssert.Contains(json, "\"Airborne\"");
            StringAssert.Contains(json, "\"LT\"");
            StringAssert.Contains(json, "\"Schema\": 1");
        }

        [TestMethod]
        public void Load_MissingFileIsAnEmptyList()
        {
            var loaded = AlertPresetStore.Load(_path);

            Assert.IsNull(loaded.Error);
            Assert.AreEqual(0, loaded.Presets.Count);
        }

        [TestMethod]
        public void Load_ReportsAnUnreadableFileInsteadOfThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ \"Schema\": 1, \"Presets\": [ { \"Text\": \"x\" } ] }");

            var loaded = AlertPresetStore.Load(_path);

            StringAssert.Contains(loaded.Error, "no alert settings");
            Assert.AreEqual(0, loaded.Presets.Count);
        }

        [TestMethod]
        public void Parse_RejectsWhatCannotBeAPresetList()
        {
            Assert.ThrowsException<InvalidDataException>(() => AlertPresetStore.Parse("not json"));
            Assert.ThrowsException<InvalidDataException>(() => AlertPresetStore.Parse("{ \"Schema\": 2, \"Presets\": [] }"));
            var twice = AlertPresetStore.Serialize(new[] { AlertPresets.EngineOut(), AlertPresets.EngineOut() });
            Assert.ThrowsException<InvalidDataException>(() => AlertPresetStore.Parse(twice));
        }

        [TestMethod]
        public void Save_ReplacesAnExistingFile()
        {
            AlertPresetStore.Save(_path, AlertPresets.All());
            AlertPresetStore.Save(_path, new List<CustomWarning> { AlertPresets.EngineOut() });

            Assert.AreEqual(1, AlertPresetStore.Load(_path).Presets.Count);
        }

        [TestMethod]
        public void SameContent_IgnoresFormattingButNotValues()
        {
            var json = AlertPresetStore.Serialize(new[] { AlertPresets.EngineOut() });
            var changed = AlertPresets.EngineOut();
            changed.Warning = 900;

            Assert.IsTrue(AlertPresetStore.SameContent(json, json.Replace("\r\n", "\n").Replace("  ", " ")));
            Assert.IsFalse(AlertPresetStore.SameContent(json, AlertPresetStore.Serialize(new[] { changed })));
            Assert.IsFalse(AlertPresetStore.SameContent(json, null));
            Assert.IsFalse(AlertPresetStore.SameContent(json, "garbage"));
        }
    }
}
