using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ConfigValueDisplayTests
    {
        [TestMethod]
        public void Preview_ShortString_ReturnedUnchanged()
        {
            Assert.AreEqual("COM5", ConfigValueDisplay.Preview("COM5"));
        }

        [TestMethod]
        public void Preview_NullString_ReturnsEmpty()
        {
            Assert.AreEqual("", ConfigValueDisplay.Preview((string)null));
        }

        [TestMethod]
        public void Preview_LongString_TruncatedWithEllipsis()
        {
            var value = new string('x', 40);
            var preview = ConfigValueDisplay.Preview(value);

            Assert.AreEqual(24, preview.Length);
            Assert.IsTrue(preview.EndsWith("..."));
            Assert.AreEqual(value.Substring(0, 21) + "...", preview);
        }

        [TestMethod]
        public void Preview_ExactlyThreshold_ReturnedUnchanged()
        {
            var value = new string('x', 24);
            Assert.AreEqual(value, ConfigValueDisplay.Preview(value));
        }

        [TestMethod]
        public void Preview_Dictionary_MissingKey_ReturnsEmpty()
        {
            var values = new Dictionary<string, string> { ["a"] = "1" };
            Assert.AreEqual("", ConfigValueDisplay.Preview(values, "b"));
        }

        [TestMethod]
        public void Preview_Dictionary_NullDictionary_ReturnsEmpty()
        {
            Assert.AreEqual("", ConfigValueDisplay.Preview(null, "a"));
        }

        [TestMethod]
        public void Preview_Dictionary_PresentKey_DelegatesToStringOverload()
        {
            var values = new Dictionary<string, string> { ["a"] = new string('y', 40) };
            Assert.AreEqual(ConfigValueDisplay.Preview(values["a"]), ConfigValueDisplay.Preview(values, "a"));
        }
    }
}
