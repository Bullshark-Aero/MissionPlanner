using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AlertColoursTests
    {
        [TestMethod]
        public void TryParse_AcceptsColourNamesAndHex()
        {
            Assert.IsTrue(AlertColours.TryParse("#ff8800", out var hex));
            Assert.AreEqual(Color.FromArgb(255, 255, 136, 0).ToArgb(), hex.ToArgb());
            Assert.IsTrue(AlertColours.TryParse("Red", out var red));
            Assert.AreEqual(Color.Red.ToArgb(), red.ToArgb());
            Assert.IsFalse(AlertColours.TryParse("NoColor", out _));
            Assert.IsFalse(AlertColours.TryParse("Bogus", out _));
            Assert.IsFalse(AlertColours.TryParse("#12345", out _));
            Assert.IsFalse(AlertColours.TryParse("#GGGGGG", out _));
            Assert.IsFalse(AlertColours.TryParse(null, out _));
        }

        [TestMethod]
        public void Normalize_KeepsNamesAndUppercasesHex()
        {
            Assert.AreEqual("#FF8800", AlertColours.Normalize("#ff8800"));
            Assert.AreEqual("Gold", AlertColours.Normalize("Gold"));
            Assert.AreEqual("Red", AlertColours.Normalize(null));
            Assert.AreEqual("Red", AlertColours.Normalize("nonsense"));
            Assert.AreEqual("#0A0B0C", AlertColours.ToText(Color.FromArgb(10, 11, 12)));
        }

        [TestMethod]
        public void ForQuickView_ColoursHexTheSameWayAsNames()
        {
            Assert.AreEqual(Color.FromArgb(255, 0, 170, 17).ToArgb(), AlertColours.ForQuickView("#00AA11").ToArgb());
            Assert.AreEqual(Color.Gold.ToArgb(), AlertColours.ForQuickView("Gold").ToArgb());
        }

        static ComboBox ColourBox(Form form) =>
            AircraftAlertsFormTestsAccess.Descendants(form).OfType<ComboBox>().Single(c => c.Items.Contains(AlertColours.CustomChoice));

        [TestMethod]
        public void ColourList_OffersTheNamedColoursThenCustom()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut() }))
            {
                var items = ColourBox(form).Items.Cast<string>().ToList();

                CollectionAssert.AreEqual(AlertColours.Named.Concat(new[] { AlertColours.CustomChoice }).ToList(), items);
                Assert.AreEqual("Red", form.SelectedColour);
            }
        }

        [TestMethod]
        public void Custom_StoresThePickedColourAsHex()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut() }))
            {
                Color offered = Color.Empty;
                form.PickColour = current =>
                {
                    offered = current;
                    return Color.FromArgb(255, 136, 0);
                };

                ColourBox(form).SelectedItem = AlertColours.CustomChoice;

                Assert.AreEqual(Color.Red.ToArgb(), offered.ToArgb(), "the picker opens on the current colour");
                Assert.AreEqual("#FF8800", form.Selected.color);
                Assert.AreEqual("#FF8800", form.SelectedColour);
                var items = ColourBox(form).Items.Cast<string>().ToList();
                Assert.AreEqual(items.Count - 2, items.IndexOf("#FF8800"), "a custom colour sits just above Custom...");
                Assert.IsTrue(form.Dirty);
            }
        }

        [TestMethod]
        public void Custom_CancelledKeepsTheColourItHad()
        {
            using (var form = new AircraftAlertsForm(new[] { AlertPresets.EngineOut() }))
            {
                form.PickColour = current => null;

                ColourBox(form).SelectedItem = AlertColours.CustomChoice;

                Assert.AreEqual("Red", form.Selected.color);
                Assert.AreEqual("Red", form.SelectedColour);
                Assert.IsFalse(form.Dirty);
            }
        }

        [TestMethod]
        public void SavedHexColour_IsShownWhenTheAlertLoads()
        {
            var engine = AlertPresets.EngineOut();
            engine.color = "#00aa11";

            using (var form = new AircraftAlertsForm(new[] { engine }))
            {
                Assert.AreEqual("#00AA11", form.SelectedColour);
                CollectionAssert.Contains(ColourBox(form).Items.Cast<string>().ToList(), "#00AA11");
            }
        }
    }

    static class AircraftAlertsFormTestsAccess
    {
        public static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
