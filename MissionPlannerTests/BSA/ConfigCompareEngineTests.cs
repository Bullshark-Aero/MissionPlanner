using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ConfigCompareEngineTests
    {
        static KeyPolicyConfig Policy() => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>
            {
                new KeyPolicyRule { Match = "speech*|distunits", Class = KeyClass.Portable },
                new KeyPolicyRule { Match = "comport*", Class = KeyClass.MachineSpecific },
                new KeyPolicyRule { Match = "mainlocx|mainlocy", Class = KeyClass.Volatile },
                new KeyPolicyRule { Match = "*password*", Class = KeyClass.Secret }
            },
            Default = KeyClass.MachineSpecific
        };

        [TestMethod]
        public void IdenticalPortableKeys_IsMatch()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0", ["speechenable"] = "True" };
            var package = new Dictionary<string, string> { ["distunits"] = "0", ["speechenable"] = "True" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsTrue(result.IsMatch);
        }

        [TestMethod]
        public void DifferentPortableValue_IsMismatch()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var package = new Dictionary<string, string> { ["distunits"] = "1" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsFalse(result.IsMatch);
            CollectionAssert.Contains(result.MismatchedKeys, "distunits");
        }

        [TestMethod]
        public void PortableKeyOnlyInLive_IsLiveOnly()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0", ["speechenable"] = "True" };
            var package = new Dictionary<string, string> { ["distunits"] = "0" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsFalse(result.IsMatch);
            CollectionAssert.Contains(result.LiveOnlyKeys, "speechenable");
        }

        [TestMethod]
        public void PortableKeyOnlyInPackage_IsPackageOnly()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0" };
            var package = new Dictionary<string, string> { ["distunits"] = "0", ["speechenable"] = "True" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsFalse(result.IsMatch);
            CollectionAssert.Contains(result.PackageOnlyKeys, "speechenable");
        }

        [TestMethod]
        public void MachineSpecificKeyChanged_NeverAppearsAsMismatch()
        {
            var live = new Dictionary<string, string> { ["comport"] = "COM3" };
            var package = new Dictionary<string, string> { ["comport"] = "COM7" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsTrue(result.IsMatch, "A MachineSpecific-class key change must never register as a mismatch.");
        }

        [TestMethod]
        public void VolatileKeyChanged_NeverAppearsAsMismatch()
        {
            var live = new Dictionary<string, string> { ["mainlocx"] = "100" };
            var package = new Dictionary<string, string> { ["mainlocx"] = "500" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsTrue(result.IsMatch, "A Volatile-class key change must never register as a mismatch.");
        }

        [TestMethod]
        public void SecretKeyChanged_NeverAppearsAsMismatch()
        {
            var live = new Dictionary<string, string> { ["some_password"] = "abc" };
            var package = new Dictionary<string, string> { ["some_password"] = "xyz" };
            var result = ConfigCompareEngine.Compare(live, package, Policy());
            Assert.IsTrue(result.IsMatch);
        }

        [TestMethod]
        public void EmptyBothSides_IsMatch()
        {
            var result = ConfigCompareEngine.Compare(new Dictionary<string, string>(), new Dictionary<string, string>(), Policy());
            Assert.IsTrue(result.IsMatch);
        }

        [TestMethod]
        public void NullDictionaries_TreatedAsEmpty_NeverThrows()
        {
            var result = ConfigCompareEngine.Compare(null, null, Policy());
            Assert.IsTrue(result.IsMatch);
        }

        [TestMethod]
        public void Normalize_ExcludesNonPortableKeys()
        {
            var live = new Dictionary<string, string> { ["distunits"] = "0", ["comport"] = "COM3", ["mainlocx"] = "1" };
            var normalized = ConfigCompareEngine.Normalize(live, Policy());
            Assert.AreEqual(1, normalized.Count);
            Assert.IsTrue(normalized.ContainsKey("distunits"));
        }
    }
}
