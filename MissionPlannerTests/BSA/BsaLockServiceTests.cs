using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaLockServiceTests
    {
        static LockPolicyConfig PolicyWithEverything() => new LockPolicyConfig
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
                    new LockActionRule { Match = "AUTH_PARAM", Class = LockClass.Authorise, InvalidatesPreflight = true }
                },
                ParamResetDefaults = new LockActionRule { Class = LockClass.Block },
                FirmwareUpload = new LockActionRule { Class = LockClass.Block },
                MpSettingChange = new List<LockActionRule>
                {
                    new LockActionRule { Match = "speechenable->false", Class = LockClass.Warn, InvalidatesPreflight = true },
                    new LockActionRule { Match = "authonly->true", Class = LockClass.Authorise, InvalidatesPreflight = true }
                },
                MissionEdit = new LockActionRule { Class = LockClass.Allow },
                PreflightConfigEdit = new LockActionRule { Class = LockClass.Block },
                LockPolicyEdit = new LockActionRule { Class = LockClass.Block }
            }
        };

        static string StampedPolicyFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "BsaLockServiceTests_policy_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{}"); // content unused - HandlePreflightResult uses loadPolicy(), not this file's content
            LockPolicyIntegrity.Stamp(path);
            return path;
        }

        /// <summary>Builds a real, minimal PreflightRunEngine that resolves to the given result and
        /// publishes it - BsaPreflightService.PublishResult takes an engine, not a raw enum, so this
        /// mirrors PreflightRunEngineTests.StatusChanged_FiresOnPublish_WithFinalResult's own pattern.</summary>
        static void Publish(BsaPreflightService service, PreflightResult desiredResult)
        {
            var check = new PreflightCheckDefinition
            {
                Id = "c1", Title = "c1", Type = CheckType.Manual, Severity = CheckSeverity.Critical, Instruction = "x"
            };
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            var engine = new PreflightRunEngine(new[] { check }, evaluator, new RegisteredCheckRegistry(), "Test Operator");

            engine.RecordResult(desiredResult == PreflightResult.Go ? CheckOutcome.Pass : CheckOutcome.Fail);
            engine.Next();
            engine.CompleteRun();

            Assert.AreEqual(desiredResult, engine.Run.Result, "Test setup produced the wrong PreflightResult - fix the fixture, not the assertion below.");
            service.PublishResult(engine);
        }

        static (BsaLockService lockService, BsaPreflightService preflightService, string policyPath, string auditDir) NewArmed()
        {
            var auditDir = Path.Combine(Path.GetTempPath(), "BsaLockServiceTests_audit_" + Guid.NewGuid().ToString("N"));
            var lockService = new BsaLockService(auditDir);
            var preflightService = new BsaPreflightService();
            var policyPath = StampedPolicyFile();

            lockService.AttachToPreflight(preflightService, () => policyPath, PolicyWithEverything);
            return (lockService, preflightService, policyPath, auditDir);
        }

        static void Cleanup(string policyPath, string auditDir)
        {
            if (File.Exists(policyPath)) File.Delete(policyPath);
            if (File.Exists(LockPolicyIntegrity.SidecarPath(policyPath))) File.Delete(LockPolicyIntegrity.SidecarPath(policyPath));
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }

        [TestMethod]
        public void FreshService_StartsOff()
        {
            var service = new BsaLockService(Path.GetTempPath());
            Assert.AreEqual(LockState.Off, service.State);
        }

        [TestMethod]
        public void FailOpen_WhenOff_CheckActionAlwaysAllows()
        {
            var service = new BsaLockService(Path.GetTempPath());
            var decision = service.CheckAction("firmware_upload", null); // policy for this action is Block, but service is Off
            Assert.AreEqual(LockClass.Allow, decision.Class);
        }

        [TestMethod]
        public void FailOpen_WhenOff_CheckParamWriteAlwaysReturnsNull()
        {
            var service = new BsaLockService(Path.GetTempPath());
            Assert.IsNull(service.CheckParamWrite("AHRS_ORIENTATION", 1.0));
        }

        [TestMethod]
        public void GoResult_ArmsLock()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                Assert.AreEqual(LockState.On, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void NoGoResult_NeverArms()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.NoGo);
                Assert.AreEqual(LockState.Off, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void TamperedPolicy_RefusesToArm()
        {
            var auditDir = Path.Combine(Path.GetTempPath(), "BsaLockServiceTests_audit_" + Guid.NewGuid().ToString("N"));
            var lockService = new BsaLockService(auditDir);
            var preflightService = new BsaPreflightService();
            var unstampedPath = Path.Combine(Path.GetTempPath(), "BsaLockServiceTests_unstamped_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(unstampedPath, "{}"); // deliberately no sidecar stamp

            lockService.AttachToPreflight(preflightService, () => unstampedPath, PolicyWithEverything);
            try
            {
                Publish(preflightService, PreflightResult.Go);
                Assert.AreEqual(LockState.Off, lockService.State);
                Assert.IsNotNull(lockService.StatusReason);
            }
            finally
            {
                File.Delete(unstampedPath);
                if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
            }
        }

        [TestMethod]
        public void WhenOn_BlockedParamWrite_ReturnsRefusalMessage()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var refusal = lockService.CheckParamWrite("AHRS_ORIENTATION", 1.0);
                Assert.IsNotNull(refusal);
                StringAssert.Contains(refusal, "AHRS_ORIENTATION");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void WhenOn_WarnParamWrite_ProceedsButInvalidates()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var refusal = lockService.CheckParamWrite("ARSPD_AUTOCAL", 1.0);
                Assert.IsNull(refusal, "WARN must never block the wire-level write - see the plan's threading analysis.");
                Assert.AreEqual(LockState.InvalidatedPending, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void WhenOn_AllowParamWrite_ReturnsNull_StaysOn()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                Assert.IsNull(lockService.CheckParamWrite("PID_ROLL_P", 1.0)); // unmatched -> Default (Allow)
                Assert.AreEqual(LockState.On, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void AfterInvalidation_ChecksFailOpenAgain_UntilReArmed()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                lockService.CheckParamWrite("ARSPD_AUTOCAL", 1.0); // invalidates
                Assert.AreEqual(LockState.InvalidatedPending, lockService.State);

                // Now behaves like Off - a normally-Blocked action must fail open.
                var decision = lockService.CheckAction("firmware_upload", null);
                Assert.AreEqual(LockClass.Allow, decision.Class);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void ReArm_AfterInvalidation_WithNewGo()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                lockService.CheckParamWrite("ARSPD_AUTOCAL", 1.0);
                Assert.AreEqual(LockState.InvalidatedPending, lockService.State);

                Publish(preflightService, PreflightResult.Go);
                Assert.AreEqual(LockState.On, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void Invalidate_WhenAlreadyOff_IsNoOp()
        {
            var service = new BsaLockService(Path.GetTempPath());
            service.Invalidate("test");
            Assert.AreEqual(LockState.Off, service.State);
        }

        [TestMethod]
        public void StatusChanged_FiresOnArm()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            LockState? observed = null;
            lockService.StatusChanged += (s, e) => observed = e.State;
            try
            {
                Publish(preflightService, PreflightResult.Go);
                Assert.AreEqual(LockState.On, observed);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void WhenOn_EvaluatedChecks_WriteAuditEntries()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                lockService.CheckParamWrite("AHRS_ORIENTATION", 1.0);

                var entries = BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow);
                Assert.AreEqual(1, entries.Count);
                Assert.AreEqual("param_write", entries[0].ActionId);
                Assert.AreEqual("Block", entries[0].Class);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void WhenOff_ChecksNeverWriteAuditEntries()
        {
            var auditDir = Path.Combine(Path.GetTempPath(), "BsaLockServiceTests_audit_" + Guid.NewGuid().ToString("N"));
            var service = new BsaLockService(auditDir);
            service.CheckAction("firmware_upload", null);
            Assert.AreEqual(0, BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow).Count);
        }

        [TestMethod]
        public void WhenOn_AuthoriseParamWrite_IsRefusedAtWire_NotDegradedToAllow()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var refusal = lockService.CheckParamWrite("AUTH_PARAM", 1.0);

                Assert.IsNotNull(refusal,
                    "An Authorise-classed param must be refused at the wire - no interactive authorisation is possible there.");
                StringAssert.Contains(refusal, "Engineering");
                Assert.AreEqual(LockState.On, lockService.State,
                    "A refused Authorise write must not invalidate the preflight - nothing proceeded.");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void WhenOn_AuthoriseCheckAction_DoesNotInvalidate_ResolutionIsDeferred()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var decision = lockService.CheckAction("mp_setting_change", "authonly->true");

                Assert.AreEqual(LockClass.Authorise, decision.Class);
                Assert.AreEqual(LockState.On, lockService.State,
                    "CheckAction must not invalidate for Authorise - authorisation hasn't been resolved yet.");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void RecordAuthoriseResolution_Authorised_InvalidatesWhenFlagged_AndAudits()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var decision = new LockDecision(LockClass.Authorise, invalidatesPreflight: true);

                lockService.RecordAuthoriseResolution("firmware_upload", null, decision, authorised: true);

                Assert.AreEqual(LockState.InvalidatedPending, lockService.State);
                var entries = BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow);
                Assert.AreEqual("Authorised", entries[entries.Count - 1].Outcome);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void RecordAuthoriseResolution_Refused_LockStaysOn_AndAudits()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var decision = new LockDecision(LockClass.Authorise, invalidatesPreflight: true);

                lockService.RecordAuthoriseResolution("firmware_upload", null, decision, authorised: false);

                Assert.AreEqual(LockState.On, lockService.State);
                var entries = BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow);
                Assert.AreEqual("AuthoriseRefused", entries[entries.Count - 1].Outcome);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void RecordOperatorReason_AppendsAuditEntryWithReason()
        {
            var (lockService, preflightService, policyPath, auditDir) = NewArmed();
            try
            {
                Publish(preflightService, PreflightResult.Go);
                var decision = new LockDecision(LockClass.Warn, invalidatesPreflight: true);

                lockService.RecordOperatorReason("mp_setting_change", "speechenable->false", decision, "field recalibration in progress");

                var entries = BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow);
                var last = entries[entries.Count - 1];
                Assert.AreEqual("ReasonRecorded", last.Outcome);
                Assert.AreEqual("field recalibration in progress", last.Reason);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }
    }
}
