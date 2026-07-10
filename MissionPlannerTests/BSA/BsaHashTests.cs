using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaHashTests
    {
        [TestMethod]
        public void SameLogicalObject_DifferentKeyOrder_SameHash()
        {
            var a = new Dictionary<string, object> { ["b"] = 2, ["a"] = 1 };
            var b = new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 };
            Assert.AreEqual(BsaHash.HashObject(a), BsaHash.HashObject(b));
        }

        [TestMethod]
        public void DifferentValues_DifferentHash()
        {
            var a = new Dictionary<string, object> { ["a"] = 1 };
            var b = new Dictionary<string, object> { ["a"] = 2 };
            Assert.AreNotEqual(BsaHash.HashObject(a), BsaHash.HashObject(b));
        }

        [TestMethod]
        public void StableAcrossCulture()
        {
            var value = new Dictionary<string, object> { ["distance"] = 1234.5 };
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                var hash1 = BsaHash.HashObject(value);

                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var hash2 = BsaHash.HashObject(value);

                Assert.AreEqual(hash1, hash2);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void ComputeSha256Hex_KnownVector_EmptyString()
        {
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                BsaHash.ComputeSha256Hex(""));
        }

        [TestMethod]
        public void HashObject_IsDeterministic_AcrossCalls()
        {
            var value = new { name = "test", version = 1 };
            Assert.AreEqual(BsaHash.HashObject(value), BsaHash.HashObject(value));
        }

        [TestMethod]
        public void NestedObjects_KeysSortedRecursively()
        {
            var a = new Dictionary<string, object>
            {
                ["outer2"] = 2,
                ["outer1"] = new Dictionary<string, object> { ["z"] = 1, ["y"] = 2 }
            };
            var b = new Dictionary<string, object>
            {
                ["outer1"] = new Dictionary<string, object> { ["y"] = 2, ["z"] = 1 },
                ["outer2"] = 2
            };
            Assert.AreEqual(BsaHash.HashObject(a), BsaHash.HashObject(b));
        }
    }
}
