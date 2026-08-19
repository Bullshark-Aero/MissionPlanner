using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// Per-view quick-view appearance is persisted as text in config.xml, so the two helpers that move a
    /// Color in and out of a settings key are the whole contract: a colour that does not survive a restart
    /// is indistinguishable from one the operator never set, and a hand-edited config must not be able to
    /// stop a panel from painting.
    /// </summary>
    [TestClass]
    public class QuickViewAppearanceTests
    {
        static readonly MethodInfo ReadColour = typeof(FlightData)
            .GetMethod("readQuickViewColor", BindingFlags.NonPublic | BindingFlags.Static);

        static readonly MethodInfo WriteColour = typeof(FlightData)
            .GetMethod("writeQuickViewColor", BindingFlags.NonPublic | BindingFlags.Static);

        const string Key = "quickViewUnitTest_valuecolor";

        Dictionary<string, string> _originalConfig;

        static Color? Read(string key)
        {
            return (Color?) ReadColour.Invoke(null, new object[] {key});
        }

        static void Write(string key, Color? colour)
        {
            WriteColour.Invoke(null, new object[] {key, colour});
        }

        [TestInitialize]
        public void SaveConfig()
        {
            // Settings.Instance lazily loads the real config.xml on first access - force that load NOW,
            // before the snapshot, or the load lands mid-test and reinstates keys a test just removed.
            var _ = Settings.Instance;
            _originalConfig = new Dictionary<string, string>(Settings.config);
        }

        [TestCleanup]
        public void RestoreConfig()
        {
            Settings.config.Clear();
            foreach (var kv in _originalConfig)
                Settings.config[kv.Key] = kv.Value;
        }

        [TestMethod]
        public void Colour_RoundTripsExactly()
        {
            var chosen = Color.FromArgb(255, 209, 151, 248);

            Write(Key, chosen);
            var back = Read(Key);

            Assert.IsNotNull(back);
            Assert.AreEqual(chosen.ToArgb(), back.Value.ToArgb());
        }

        [TestMethod]
        public void Colour_IsStoredAsEightDigitHex()
        {
            Write(Key, Color.FromArgb(255, 0, 0, 0));

            // config.xml is hand-edited in the field often enough that the stored form should be readable
            Assert.AreEqual("FF000000", Settings.Instance[Key]);
        }

        [TestMethod]
        public void HighAlphaColour_SurvivesTheInt32Boundary()
        {
            // opaque white is 0xFFFFFFFF, which does not fit a signed int - it round trips only because the
            // read parses as hex (wrapping to -1) rather than as a decimal number, which would throw
            var white = Color.FromArgb(255, 255, 255, 255);

            Write(Key, white);
            Assert.AreEqual("FFFFFFFF", Settings.Instance[Key]);

            var back = Read(Key);
            Assert.IsNotNull(back);
            Assert.AreEqual(white.ToArgb(), back.Value.ToArgb());
        }

        [TestMethod]
        public void UnsetKey_ReadsAsNull()
        {
            Settings.Instance.Remove(Key);

            // null is what "the operator has not chosen one, leave the theme alone" looks like
            Assert.IsNull(Read(Key));
        }

        [TestMethod]
        public void MalformedValue_ReadsAsNullRatherThanThrowing()
        {
            Settings.Instance[Key] = "not-a-colour";

            Assert.IsNull(Read(Key));
        }

        [TestMethod]
        public void EmptyValue_ReadsAsNull()
        {
            Settings.Instance[Key] = "";

            Assert.IsNull(Read(Key));
        }

        [TestMethod]
        public void WritingNull_RemovesTheKeyEntirely()
        {
            Write(Key, Color.Red);
            Assert.IsTrue(Settings.Instance.ContainsKey(Key));

            // the reset-to-theme path must not leave a stale key that would win on the next start
            Write(Key, null);
            Assert.IsFalse(Settings.Instance.ContainsKey(Key));
            Assert.IsNull(Read(Key));
        }

        [TestMethod]
        public void LabelAndValueColours_AreIndependentKeys()
        {
            const string labelKey = "quickViewUnitTest_labelcolor";

            Write(labelKey, Color.FromArgb(255, 10, 20, 30));
            Write(Key, Color.FromArgb(255, 40, 50, 60));

            Assert.AreEqual(Color.FromArgb(255, 10, 20, 30).ToArgb(), Read(labelKey).Value.ToArgb());
            Assert.AreEqual(Color.FromArgb(255, 40, 50, 60).ToArgb(), Read(Key).Value.ToArgb());

            // clearing one must not disturb the other
            Write(labelKey, null);
            Assert.IsNull(Read(labelKey));
            Assert.IsNotNull(Read(Key));
        }
    }
}
