using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class LockActionMatcherTests
    {
        static LockPolicyConfig Policy() => new LockPolicyConfig
        {
            SchemaVersion = 1,
            PolicyVersion = "1.0.0",
            Default = LockClass.Allow,
            Actions = new LockPolicyActions
            {
                ParamWrite = new List<LockActionRule>
                {
                    new LockActionRule { Match = "AHRS_ORIENTATION", Class = LockClass.Block, InvalidatesPreflight = true },
                    new LockActionRule { Match = "ARSPD_AUTOCAL", Class = LockClass.Warn, InvalidatesPreflight = true },
                    new LockActionRule { Match = "TRIM_ARSPD_CM|AIRSPEED_CRUISE", Class = LockClass.Allow },
                    // Synthetic Authorise row - the shipped default doesn't use this class, but the
                    // engine must support and unit-test it (WP3 acceptance criterion: "policy supports
                    // ALLOW/WARN/BLOCK/AUTHORISE").
                    new LockActionRule { Match = "SIMULATED_AUTHORISE_PARAM", Class = LockClass.Authorise }
                },
                ParamResetDefaults = new LockActionRule { Class = LockClass.Block },
                FirmwareUpload = new LockActionRule { Class = LockClass.Block },
                MpSettingChange = new List<LockActionRule>
                {
                    new LockActionRule { Match = "speechenable->false", Class = LockClass.Warn, InvalidatesPreflight = true }
                },
                MissionEdit = new LockActionRule { Class = LockClass.Allow },
                PreflightConfigEdit = new LockActionRule { Class = LockClass.Block },
                LockPolicyEdit = new LockActionRule { Class = LockClass.Block }
            }
        };

        [TestMethod]
        public void ParamWrite_MatchedBlock_ReturnsBlock()
        {
            var decision = LockActionMatcher.MatchParamWrite("AHRS_ORIENTATION", Policy());
            Assert.AreEqual(LockClass.Block, decision.Class);
        }

        [TestMethod]
        public void ParamWrite_MatchedWarn_ReturnsWarnAndInvalidates()
        {
            var decision = LockActionMatcher.MatchParamWrite("ARSPD_AUTOCAL", Policy());
            Assert.AreEqual(LockClass.Warn, decision.Class);
            Assert.IsTrue(decision.InvalidatesPreflight);
        }

        [TestMethod]
        public void ParamWrite_PipeAlternative_Matches()
        {
            Assert.AreEqual(LockClass.Allow, LockActionMatcher.MatchParamWrite("AIRSPEED_CRUISE", Policy()).Class);
            Assert.AreEqual(LockClass.Allow, LockActionMatcher.MatchParamWrite("TRIM_ARSPD_CM", Policy()).Class);
        }

        [TestMethod]
        public void ParamWrite_Authorise_ReturnsAuthorise()
        {
            Assert.AreEqual(LockClass.Authorise, LockActionMatcher.MatchParamWrite("SIMULATED_AUTHORISE_PARAM", Policy()).Class);
        }

        [TestMethod]
        public void ParamWrite_Unmatched_FallsToDefault()
        {
            Assert.AreEqual(LockClass.Allow, LockActionMatcher.MatchParamWrite("PID_ROLL_P", Policy()).Class);
        }

        [TestMethod]
        public void MpSettingChange_SpeechDisabled_ReturnsWarn()
        {
            var decision = LockActionMatcher.MatchMpSettingChange("speechenable->false", Policy());
            Assert.AreEqual(LockClass.Warn, decision.Class);
            Assert.IsTrue(decision.InvalidatesPreflight);
        }

        [TestMethod]
        public void ResolveSingle_FirmwareUpload_ReturnsBlock()
        {
            Assert.AreEqual(LockClass.Block, LockActionMatcher.ResolveSingle(Policy().Actions.FirmwareUpload, Policy()).Class);
        }

        [TestMethod]
        public void ResolveSingle_MissionEdit_ReturnsAllow()
        {
            Assert.AreEqual(LockClass.Allow, LockActionMatcher.ResolveSingle(Policy().Actions.MissionEdit, Policy()).Class);
        }

        // FirmwareUpload and ParamResetDefaults are single-shaped actions like MissionEdit -
        // ResolveSingle is class-agnostic, so an Authorise row here proves the engine resolves it
        // correctly for these two action ids specifically, same as it already does for MissionEdit
        // above. The interactive passphrase prompt itself lives in BSA.UI.LockGateUi (WinForms
        // dialogs, not unit-testable here) and is the same AllowedToProceed() call FlightPlanner's
        // mission_edit gate already uses - live-verified via the real GUI, not re-tested here.
        [TestMethod]
        public void ResolveSingle_FirmwareUpload_Authorise_ReturnsAuthorise()
        {
            var rule = new LockActionRule { Class = LockClass.Authorise, InvalidatesPreflight = true };
            var decision = LockActionMatcher.ResolveSingle(rule, Policy());
            Assert.AreEqual(LockClass.Authorise, decision.Class);
            Assert.IsTrue(decision.InvalidatesPreflight);
        }

        [TestMethod]
        public void ResolveSingle_ParamResetDefaults_Authorise_ReturnsAuthorise()
        {
            var rule = new LockActionRule { Class = LockClass.Authorise };
            Assert.AreEqual(LockClass.Authorise, LockActionMatcher.ResolveSingle(rule, Policy()).Class);
        }

        [TestMethod]
        public void ResolveSingle_NullRule_FallsToDefault()
        {
            Assert.AreEqual(LockClass.Allow, LockActionMatcher.ResolveSingle(null, Policy()).Class);
        }
    }
}
