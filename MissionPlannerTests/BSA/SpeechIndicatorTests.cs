using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class SpeechIndicatorTests
    {
        [TestMethod]
        public void Dot_FollowsTheSpeechSetting()
        {
            var on = true;
            using (var indicator = new SpeechIndicator(() => on))
            {
                Assert.IsTrue(indicator.SpeechOn);
                Assert.AreEqual(SpeechIndicator.OnColour, indicator.DotColour);

                on = false;
                indicator.Poll();

                Assert.IsFalse(indicator.SpeechOn);
                Assert.AreEqual(SpeechIndicator.OffColour, indicator.DotColour);
            }
        }

        [TestMethod]
        public void Dot_ShowsOffWhenTheSettingCannotBeRead()
        {
            using (var indicator = new SpeechIndicator(() => throw new InvalidOperationException()))
                Assert.AreEqual(SpeechIndicator.OffColour, indicator.DotColour);
        }
    }
}
