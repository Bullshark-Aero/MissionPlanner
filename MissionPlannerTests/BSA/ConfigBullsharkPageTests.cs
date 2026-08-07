using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.UI;

namespace MissionPlanner.BSA.Tests
{
    /// <summary>
    /// The page is the single entry point for every BSA config action, so the thing worth guarding is
    /// that constructing it stays free of MP globals (no MainV2.comPort / Settings) and that all six
    /// actions are actually wired up. The click handlers themselves need a live MP process and stay
    /// uncovered, exactly as they were before the move.
    /// </summary>
    [TestClass]
    public class ConfigBullsharkPageTests
    {
        static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }

        [TestMethod]
        public void Construction_NeedsNoMissionPlannerGlobals()
        {
            // Would throw if the ctor reached for MainV2.comPort or Settings.Instance.
            using (var page = new ConfigBullsharkPage())
                Assert.IsTrue(page.Controls.Count > 0);
        }

        [TestMethod]
        public void Page_ExposesAllSixConfigActions()
        {
            using (var page = new ConfigBullsharkPage())
            {
                var buttonTexts = Descendants(page).OfType<Button>().Select(b => b.Text).ToList();

                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "Import Config...",
                        "Restore Previous...",
                        "Compare to Package...",
                        "Export MP Config",
                        "Change Passphrase...",
                        "Edit Lock Policy..."
                    },
                    buttonTexts);
            }
        }

        /// <summary>Sections are a bordered plain Panel (exact type, not a FlowLayoutPanel/other
        /// subclass) with a bold Label as its title - NOT a GroupBox. A live screenshot against the
        /// running app showed GroupBox.Text renders invisibly here (native Visual Styles caption
        /// rendering ignores WinForms ForeColor entirely), so this page deliberately avoids GroupBox -
        /// see ConfigBullsharkPage.BuildSection's doc comment.</summary>
        static IEnumerable<Panel> Sections(Control page) =>
            Descendants(page).Where(c => c.GetType() == typeof(Panel)).Cast<Panel>();

        static string SectionTitle(Panel section) =>
            Descendants(section).OfType<Label>().First(l => l.Font.Bold).Text;

        [TestMethod]
        public void Page_GroupsActionsUnderLabelledSections()
        {
            using (var page = new ConfigBullsharkPage())
            {
                var groupTitles = Sections(page).Select(SectionTitle).ToList();

                CollectionAssert.AreEquivalent(
                    new[] { "Approved Configuration", "Authoring & Engineering" },
                    groupTitles);
            }
        }

        /// <summary>
        /// The page stacks AutoSize section Panels inside a FlowLayoutPanel, which WinForms is happy to
        /// collapse to zero height if the AutoSize chain is set up wrong - and the failure is silent,
        /// showing an empty page rather than throwing. Host it the way BackstageViewPage does
        /// (Dock=Fill, AutoScroll=true, ExtLibs/Controls/BackstageView/BackstageViewPage.cs:56-64),
        /// force a real layout pass, and check the sections actually occupy space and don't overlap.
        /// </summary>
        [TestMethod]
        public void HostedLikeABackstagePage_SectionsLayOutWithRealSize()
        {
            using (var form = new Form { ClientSize = new Size(820, 560) })
            using (var page = new ConfigBullsharkPage())
            {
                page.Dock = DockStyle.Fill;
                page.AutoScroll = true;
                form.Controls.Add(page);
                form.CreateControl();
                form.PerformLayout();

                var sections = Sections(page).OrderBy(s => s.Top).ToList();
                Assert.AreEqual(2, sections.Count);

                foreach (var section in sections)
                {
                    Assert.IsTrue(section.Height > 60, $"'{SectionTitle(section)}' collapsed to {section.Height}px high.");
                    Assert.IsTrue(section.Width > 200, $"'{SectionTitle(section)}' collapsed to {section.Width}px wide.");
                }

                Assert.IsTrue(sections[0].Bottom <= sections[1].Top,
                    "The two sections overlap vertically.");
            }
        }

        [TestMethod]
        public void EveryActionButton_HasADescriptionBesideIt()
        {
            using (var page = new ConfigBullsharkPage())
            {
                foreach (var table in Descendants(page).OfType<TableLayoutPanel>())
                {
                    foreach (var button in table.Controls.OfType<Button>())
                    {
                        var position = table.GetPositionFromControl(button);
                        var description = table.GetControlFromPosition(position.Column + 1, position.Row) as Label;

                        Assert.IsNotNull(description, $"'{button.Text}' has no description label beside it.");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(description.Text),
                            $"'{button.Text}' has an empty description label.");
                        Assert.IsTrue(button.Right <= description.Left,
                            $"'{button.Text}' overlaps its description label.");
                    }
                }
            }
        }
    }
}
