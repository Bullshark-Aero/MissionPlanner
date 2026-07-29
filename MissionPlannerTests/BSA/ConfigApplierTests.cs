using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ConfigApplierTests
    {
        [TestMethod]
        public void Apply_WritesOnlyApprovedKeys_LeavesOthersUntouched()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0", ["comport"] = "COM3" };
            var approved = new Dictionary<string, string> { ["distunits"] = "1" };

            ConfigApplier.Apply(live, approved);

            Assert.AreEqual("1", live["distunits"]);
            Assert.AreEqual("COM3", live["comport"], "Unselected keys must never be touched by Apply.");
        }

        [TestMethod]
        public void Apply_NewKey_IsAdded()
        {
            var live = new Dictionary<string, string>();
            var approved = new Dictionary<string, string> { ["speechenable"] = "True" };

            ConfigApplier.Apply(live, approved);

            Assert.AreEqual("True", live["speechenable"]);
        }

        [TestMethod]
        public void Apply_ReturnsOnlyKeysThatActuallyChanged()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var approved = new Dictionary<string, string> { ["distunits"] = "0", ["altunits"] = "1" };

            var changed = ConfigApplier.Apply(live, approved);

            CollectionAssert.DoesNotContain(changed, "distunits", "Applying an identical value must not be reported as a change.");
            CollectionAssert.Contains(changed, "altunits");
            Assert.AreEqual(1, changed.Count);
        }

        [TestMethod]
        public void Apply_EmptyApprovedSet_ChangesNothing()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var changed = ConfigApplier.Apply(live, new Dictionary<string, string>());

            Assert.AreEqual(0, changed.Count);
            Assert.AreEqual("0", live["distunits"]);
        }

        [TestMethod]
        public void Apply_CoupledPairAppliedTogether_BothLand()
        {
            var live = new Dictionary<string, string> { ["guided_alt"] = "50", ["guided_alt_frame"] = "0" };
            var approved = new Dictionary<string, string> { ["guided_alt"] = "100", ["guided_alt_frame"] = "3" };

            var changed = ConfigApplier.Apply(live, approved);

            Assert.AreEqual(2, changed.Count);
            Assert.AreEqual("100", live["guided_alt"]);
            Assert.AreEqual("3", live["guided_alt_frame"]);
        }
    }
}
