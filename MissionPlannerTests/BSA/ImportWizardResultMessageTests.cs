using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ImportWizardResultMessageTests
    {
        [TestMethod]
        public void AlreadyInstalled_SaysNothingChanged_AndAsksForNoRestart()
        {
            var message = ImportWizardForm.ResultMessage(new BsaBundleApplyResult
            {
                NoChangesRequired = true,
                Status = BsaTransactionStatus.Committed,
                TransactionDirectory = @"C:\tx\existing",
                ChangedSettings = new List<string>()
            }, @"C:\backups\b.bsampconfig");

            StringAssert.Contains(message, "already installed");
            StringAssert.Contains(message, "no restart is needed");
            StringAssert.Contains(message, @"C:\tx\existing");
            Assert.IsFalse(message.Contains("Restart Mission Planner"));
            Assert.IsFalse(message.Contains("staged"));
        }

        [TestMethod]
        public void StagedImport_ReportsChangesAndAsksForRestart()
        {
            var message = ImportWizardForm.ResultMessage(new BsaBundleApplyResult
            {
                Status = BsaTransactionStatus.PendingRestart,
                RestartRequired = true,
                TransactionDirectory = @"C:\tx\new",
                ChangedSettings = new List<string> { "distunits", "speedunits" },
                WarningsInstalled = true
            }, @"C:\backups\b.bsampconfig");

            StringAssert.Contains(message, "Bundle staged successfully. 2 setting(s) changed.");
            StringAssert.Contains(message, "Restart Mission Planner to verify and commit the installation.");
            StringAssert.Contains(message, "The imported warnings are already active");
        }
    }
}
