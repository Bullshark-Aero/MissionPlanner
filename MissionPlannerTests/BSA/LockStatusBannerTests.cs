using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;
using MissionPlanner.BSA.Lock;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class LockStatusBannerTests
    {
        [TestMethod]
        public void FreshBanner_ShowsOffAndUnknown()
        {
            var banner = new LockStatusBanner();
            StringAssert.Contains(banner.DisplayText, "OFF");
            StringAssert.Contains(banner.DisplayText, "Unknown");
        }

        [TestMethod]
        public void SetStatus_On_ShowsOnAndGo_RedBackground()
        {
            var banner = new LockStatusBanner();
            banner.SetStatus(LockState.On, null, PreflightResult.Go, "1.0.0");

            StringAssert.Contains(banner.DisplayText, "ON");
            StringAssert.Contains(banner.DisplayText, "Go");
            StringAssert.Contains(banner.DisplayText, "1.0.0");
            Assert.AreEqual(Color.Red, banner.BackColor);
        }

        [TestMethod]
        public void SetStatus_InvalidatedPending_ShowsReason_KhakiBackground()
        {
            var banner = new LockStatusBanner();
            banner.SetStatus(LockState.InvalidatedPending, "Parameter 'ARSPD_AUTOCAL' changed while locked.", PreflightResult.Go, "1.0.0");

            StringAssert.Contains(banner.DisplayText, "INVALIDATED");
            StringAssert.Contains(banner.DisplayText, "ARSPD_AUTOCAL");
            Assert.AreEqual(Color.Khaki, banner.BackColor);
        }

        [TestMethod]
        public void SetStatus_Off_GrayBackground()
        {
            var banner = new LockStatusBanner();
            banner.SetStatus(LockState.Off, null, PreflightResult.NoGo, null);

            StringAssert.Contains(banner.DisplayText, "OFF");
            StringAssert.Contains(banner.DisplayText, "NoGo");
            Assert.AreEqual(Color.Gainsboro, banner.BackColor);
        }

        [TestMethod]
        public void AttachToServices_RealArmViaGoPreflight_BannerUpdates()
        {
            var auditDir = Path.Combine(Path.GetTempPath(), "LockStatusBannerTests_" + Guid.NewGuid().ToString("N"));
            var policyPath = Path.Combine(Path.GetTempPath(), "LockStatusBannerTests_policy_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(policyPath, "{}");
            LockPolicyIntegrity.Stamp(policyPath);
            try
            {
                var preflightService = new BsaPreflightService();
                var lockService = new BsaLockService(auditDir);
                lockService.AttachToPreflight(preflightService, () => policyPath, () => new LockPolicyConfig
                {
                    SchemaVersion = 1,
                    PolicyVersion = "2.5.0",
                    Default = LockClass.Allow,
                    Actions = new LockPolicyActions
                    {
                        ParamResetDefaults = new LockActionRule { Class = LockClass.Block },
                        FirmwareUpload = new LockActionRule { Class = LockClass.Block },
                        MissionEdit = new LockActionRule { Class = LockClass.Allow },
                        PreflightConfigEdit = new LockActionRule { Class = LockClass.Block },
                        LockPolicyEdit = new LockActionRule { Class = LockClass.Block }
                    }
                });

                var banner = new LockStatusBanner();
                banner.AttachToServices(preflightService, lockService);

                // Real GO preflight, mirroring BsaLockServiceTests' own pattern.
                var check = new PreflightCheckDefinition { Id = "c1", Title = "c1", Type = CheckType.Manual, Severity = CheckSeverity.Critical, Instruction = "x" };
                var evaluator = new AutoCheckEvaluator(new Dictionary<CheckSource, IValueProvider>());
                var engine = new PreflightRunEngine(new[] { check }, evaluator, new RegisteredCheckRegistry(), "Test Operator");
                engine.RecordResult("c1", CheckOutcome.Pass);
                engine.Next();
                engine.CompleteRun();
                preflightService.PublishResult(engine);

                Assert.AreEqual(LockState.On, lockService.State);
                StringAssert.Contains(banner.DisplayText, "ON");
                StringAssert.Contains(banner.DisplayText, "2.5.0");
            }
            finally
            {
                File.Delete(policyPath);
                File.Delete(LockPolicyIntegrity.SidecarPath(policyPath));
                if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
            }
        }
    }
}
