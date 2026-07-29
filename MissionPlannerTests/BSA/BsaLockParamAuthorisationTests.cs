using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// BeginParamWriteAuthorisation: the pre-authorisation scope that lets a gated UI surface
    /// (LockGateUi.AuthoriseParamWrites) turn an Authorise-classed parameter refusal into a
    /// credentialed, audited write at the wire hook. Fixture mirrors BsaLockServiceTests (real
    /// arm via a GO preflight against a stamped policy file).
    /// </summary>
    [TestClass]
    public class BsaLockParamAuthorisationTests
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
                    new LockActionRule { Match = "BLOCK_PARAM", Class = LockClass.Block, InvalidatesPreflight = true },
                    new LockActionRule { Match = "AUTH_PARAM|AUTH_PARAM2", Class = LockClass.Authorise, InvalidatesPreflight = true },
                    new LockActionRule { Match = "AUTH_NOINVAL", Class = LockClass.Authorise }
                },
                ParamResetDefaults = new LockActionRule { Class = LockClass.Block },
                FirmwareUpload = new LockActionRule { Class = LockClass.Block },
                MissionEdit = new LockActionRule { Class = LockClass.Allow },
                PreflightConfigEdit = new LockActionRule { Class = LockClass.Block },
                LockPolicyEdit = new LockActionRule { Class = LockClass.Block }
            }
        };

        static void PublishGo(BsaPreflightService service)
        {
            var check = new PreflightCheckDefinition
            {
                Id = "c1", Title = "c1", Type = CheckType.Manual, Severity = CheckSeverity.Critical, Instruction = "x"
            };
            var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
            var engine = new PreflightRunEngine(new[] { check }, evaluator, new RegisteredCheckRegistry(), "Test Operator");
            engine.RecordResult("c1", CheckOutcome.Pass);
            engine.Next();
            engine.CompleteRun();
            service.PublishResult(engine);
        }

        static (BsaLockService lockService, string policyPath, string auditDir) NewArmed()
        {
            var auditDir = Path.Combine(Path.GetTempPath(), "BsaLockParamAuthTests_audit_" + Guid.NewGuid().ToString("N"));
            var policyPath = Path.Combine(Path.GetTempPath(), "BsaLockParamAuthTests_policy_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(policyPath, "{}");
            LockPolicyIntegrity.Stamp(policyPath);

            var lockService = new BsaLockService(auditDir);
            var preflightService = new BsaPreflightService();
            lockService.AttachToPreflight(preflightService, () => policyPath, Policy);
            PublishGo(preflightService);
            Assert.AreEqual(LockState.On, lockService.State, "Fixture failed to arm the lock.");
            return (lockService, policyPath, auditDir);
        }

        static void Cleanup(string policyPath, string auditDir)
        {
            if (File.Exists(policyPath)) File.Delete(policyPath);
            if (File.Exists(LockPolicyIntegrity.SidecarPath(policyPath))) File.Delete(LockPolicyIntegrity.SidecarPath(policyPath));
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }

        static List<AuditEntry> TodaysAudit(string auditDir) => BsaAuditLog.ReadDay(auditDir, DateTime.UtcNow);

        [TestMethod]
        public void AuthoriseParam_WithoutScope_IsRefused()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                var refusal = lockService.CheckParamWrite("AUTH_PARAM", 1.0);
                StringAssert.Contains(refusal, "Engineering Mode authorisation");
                Assert.AreEqual(LockState.On, lockService.State, "A refused write must not invalidate the preflight.");
                Assert.AreEqual("Blocked", TodaysAudit(auditDir).Last().Outcome);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void AuthoriseParam_InsideScope_ProceedsAuditedAndInvalidates()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "AUTH_PARAM" }))
                {
                    Assert.IsNull(lockService.CheckParamWrite("AUTH_PARAM", 1.0), "Authorised write must be allowed.");
                }

                var last = TodaysAudit(auditDir).Last();
                Assert.AreEqual("Authorised", last.Outcome);
                Assert.AreEqual("AUTH_PARAM", last.MatchValue);

                Assert.AreEqual(LockState.InvalidatedPending, lockService.State, "InvalidatesPreflight rule must fire on the authorised write.");
                StringAssert.Contains(lockService.StatusReason, "Engineering authorisation");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void AuthoriseParam_NoInvalidateRule_ProceedsWithoutInvalidating()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "AUTH_NOINVAL" }))
                {
                    Assert.IsNull(lockService.CheckParamWrite("AUTH_NOINVAL", 1.0));
                }
                Assert.AreEqual(LockState.On, lockService.State);
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void ScopeDisposed_SubsequentWriteIsRefusedAgain()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "AUTH_NOINVAL" }))
                {
                    Assert.IsNull(lockService.CheckParamWrite("AUTH_NOINVAL", 1.0));
                }

                var refusal = lockService.CheckParamWrite("AUTH_NOINVAL", 2.0);
                StringAssert.Contains(refusal, "Engineering Mode authorisation");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void BlockParam_InsideScope_StaysRefused()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "BLOCK_PARAM" }))
                {
                    var refusal = lockService.CheckParamWrite("BLOCK_PARAM", 1.0);
                    StringAssert.Contains(refusal, "blocked while locked");
                }
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void Scope_IsCaseInsensitive_AndCoversPipeAlternatives()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "auth_param", "AUTH_NOINVAL" }))
                {
                    Assert.IsNull(lockService.CheckParamWrite("AUTH_PARAM", 1.0), "Grant lookup must be case-insensitive.");
                }
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void UngrantedAuthoriseParam_InsideScopeForOtherNames_StaysRefused()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                using (lockService.BeginParamWriteAuthorisation(new[] { "AUTH_NOINVAL" }))
                {
                    var refusal = lockService.CheckParamWrite("AUTH_PARAM2", 1.0);
                    StringAssert.Contains(refusal, "Engineering Mode authorisation");
                }
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void OverlappingScopes_GrantSurvivesUntilLastDispose_AndDoubleDisposeIsSafe()
        {
            var (lockService, policyPath, auditDir) = NewArmed();
            try
            {
                var outer = lockService.BeginParamWriteAuthorisation(new[] { "AUTH_NOINVAL" });
                var inner = lockService.BeginParamWriteAuthorisation(new[] { "AUTH_NOINVAL" });

                inner.Dispose();
                inner.Dispose(); // double-dispose must not steal the outer scope's grant
                Assert.IsNull(lockService.CheckParamWrite("AUTH_NOINVAL", 1.0), "Outer scope's grant must survive inner dispose.");

                outer.Dispose();
                StringAssert.Contains(lockService.CheckParamWrite("AUTH_NOINVAL", 2.0), "Engineering Mode authorisation");
            }
            finally
            {
                Cleanup(policyPath, auditDir);
            }
        }

        [TestMethod]
        public void Scope_WhileLockOff_IsHarmless()
        {
            var service = new BsaLockService(Path.GetTempPath());
            using (service.BeginParamWriteAuthorisation(new[] { "ANY_PARAM" }))
            {
                Assert.IsNull(service.CheckParamWrite("ANY_PARAM", 1.0), "Fail-open when Off, with or without a scope.");
            }
        }
    }
}
