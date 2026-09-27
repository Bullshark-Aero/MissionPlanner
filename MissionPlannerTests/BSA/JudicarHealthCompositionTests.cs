using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Telemetry;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class JudicarHealthCompositionTests
    {
        string _path;

        [TestInitialize]
        public void Init() =>
            _path = Path.Combine(Path.GetTempPath(), "bsa-health-rules-" + Guid.NewGuid().ToString("N") + ".json");

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        static BsaHealthRuleSet JudicarRules() =>
            Judicar2600BundleProfile.Create(new BsaQuickViewProfile
            {
                Rows = 1, Columns = 1,
                Cells = { new BsaQuickViewCell { Position = 1, SourceId = "MAV_ESC_HOT" } }
            }).HealthRules;

        [TestMethod]
        public void LoadRules_ValidFile_ReturnsTheRules()
        {
            var expected = JudicarRules();
            File.WriteAllText(_path, JsonConvert.SerializeObject(expected));

            var loaded = JudicarHealthComposition.LoadRules(_path);

            Assert.AreEqual(expected.EvaluationHz, loaded.EvaluationHz);
            Assert.AreEqual(expected.Rules.Count, loaded.Rules.Count);
        }

        [TestMethod]
        public void LoadRules_EmptyFile_Throws()
        {
            File.WriteAllText(_path, "");
            Assert.ThrowsException<InvalidDataException>(() => JudicarHealthComposition.LoadRules(_path));
        }

        [TestMethod]
        public void LoadRules_JsonNull_Throws()
        {
            File.WriteAllText(_path, "null");
            Assert.ThrowsException<InvalidDataException>(() => JudicarHealthComposition.LoadRules(_path));
        }

        [TestMethod]
        public void LoadRules_MalformedJson_Throws()
        {
            File.WriteAllText(_path, "{ \"EvaluationHz\": ");
            Exception thrown = null;
            try { JudicarHealthComposition.LoadRules(_path); }
            catch (Exception ex) { thrown = ex; }
            Assert.IsInstanceOfType(thrown, typeof(JsonException));
        }

        [TestMethod]
        public void LoadRules_RulesThatFailValidation_Throws()
        {
            var rules = JudicarRules();
            rules.EvaluationHz = 50;
            File.WriteAllText(_path, JsonConvert.SerializeObject(rules));
            Assert.ThrowsException<InvalidDataException>(() => JudicarHealthComposition.LoadRules(_path));
        }

        [TestMethod]
        public void HealthFields_ReadNotOk_UntilTheServicePublishes()
        {
            var state = new CurrentState();

            Assert.AreEqual(0f, state.J26_DATA_OK);
            Assert.AreEqual(0f, state.J26_ESC_OK);
            Assert.AreEqual(0f, state.J26_GPS_RED_OK);
        }
    }
}
