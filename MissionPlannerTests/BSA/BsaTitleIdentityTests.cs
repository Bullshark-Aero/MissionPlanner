using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Identity;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaTitleIdentityTests
    {
        const string BaseTitle = "BullShark Mission Planner 1.3.83";
        const string SerialBanner = "CubeOrangePlus 0031001F 30325104 33383839";
        const string CanonicalUid = "1F0031000451323039383833";

        [TestMethod]
        public void BaseTitleContainsProductAndBsmpVersionOnly()
        {
            Assert.AreEqual(BaseTitle,
                BsaTitleIdentity.BuildBaseTitle("BullShark Mission Planner", "1.3.83"));
        }

        [TestMethod]
        public void ExplicitBsaSemverProducesLayeredFirmwareIdentity()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(
                BaseTitle,
                "ArduPlane V4.7.0 (98cc2c8f) BSA V1.5.0",
                SerialBanner,
                CanonicalUid);

            Assert.AreEqual(
                BaseTitle + " ArduPlane V1.5.0 Based on V4.7.0 CubeOrangePlus " + CanonicalUid,
                title);
        }

        [TestMethod]
        public void BsaPrereleaseAndBuildMetadataAreAccepted()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(
                BaseTitle,
                "ArduPlane V4.7.0 BSA V1.5.0-rc.1+bench.2",
                SerialBanner,
                CanonicalUid);

            StringAssert.Contains(title, "ArduPlane V1.5.0-rc.1+bench.2 Based on V4.7.0");
        }

        [TestMethod]
        public void OrdinaryFirmwareIsReportedWithoutInventingABsaVersion()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(
                BaseTitle,
                "ArduPlane V4.7.0 (98cc2c8f)",
                SerialBanner,
                CanonicalUid);

            Assert.AreEqual(
                BaseTitle + " ArduPlane V4.7.0 (98cc2c8f) CubeOrangePlus " + CanonicalUid,
                title);
            Assert.IsFalse(title.Contains("Based on"));
        }

        [TestMethod]
        public void InvalidBsaVersionIsNotPresentedAsAuthoritative()
        {
            var firmware = "ArduPlane V4.7.0 BSA V1.05.0";
            var title = BsaTitleIdentity.BuildConnectedTitle(BaseTitle, firmware, SerialBanner, CanonicalUid);

            StringAssert.Contains(title, firmware);
            Assert.IsFalse(title.Contains("Based on"));
        }

        [TestMethod]
        public void CanonicalUid2TakesPriorityOverLegacySerialWords()
        {
            const string uid2 = "AABBCCDDEEFF001122334455";

            var title = BsaTitleIdentity.BuildConnectedTitle(BaseTitle, null, SerialBanner, uid2);

            Assert.AreEqual(BaseTitle + " CubeOrangePlus " + uid2, title);
            Assert.IsFalse(title.Contains(CanonicalUid));
        }

        [TestMethod]
        public void LegacySerialWordsProvideCanonicalFallbackUid()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(BaseTitle, null, SerialBanner, null);

            Assert.AreEqual(BaseTitle + " CubeOrangePlus " + CanonicalUid, title);
        }

        [TestMethod]
        public void MavlinkUid2ZeroPaddingIsNotShownWhenLegacyIdentityConfirmsItsLength()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(
                BaseTitle,
                null,
                SerialBanner,
                CanonicalUid + "000000000000");

            Assert.AreEqual(BaseTitle + " CubeOrangePlus " + CanonicalUid, title);
        }

        [TestMethod]
        public void MissingAutopilotIdentityLeavesDisconnectedTitle()
        {
            Assert.AreEqual(BaseTitle,
                BsaTitleIdentity.BuildConnectedTitle(BaseTitle, null, null, null));
        }

        [TestMethod]
        public void BannerWhitespaceCannotCorruptTheWindowTitle()
        {
            var title = BsaTitleIdentity.BuildConnectedTitle(
                BaseTitle,
                " ArduPlane   V4.7.0\r\nBSA V1.5.0 ",
                SerialBanner,
                CanonicalUid.ToLowerInvariant());

            Assert.AreEqual(
                BaseTitle + " ArduPlane V1.5.0 Based on V4.7.0 CubeOrangePlus " + CanonicalUid,
                title);
        }
    }
}
