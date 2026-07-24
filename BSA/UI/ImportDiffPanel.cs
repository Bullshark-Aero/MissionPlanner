using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Per-key-group diff preview with checkboxes (source-doc requirement: "import must never blindly
    /// overwrite"). One row per ConfigDiffGroup, not per key - a coupled pair like guided_alt/
    /// guided_alt_frame is one checkable row, never two, so it can't be half-applied. All rows start
    /// unchecked; the operator opts in explicitly. LiveOnly-only groups (nothing in the package to
    /// apply) are shown for information but have no meaningful checkbox action.
    /// </summary>
    public class ImportDiffPanel : Panel
    {
        readonly CheckedListBox _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        readonly Button _selectAll = new Button { Text = "Select All", AutoSize = true };
        readonly Button _selectNone = new Button { Text = "Select None", AutoSize = true };

        List<ConfigDiffGroup> _groups = new List<ConfigDiffGroup>();

        public ImportDiffPanel()
        {
            Dock = DockStyle.Fill;

            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            _selectAll.Click += (s, e) => SelectAll();
            _selectNone.Click += (s, e) => SelectNone();
            buttonRow.Controls.Add(_selectAll);
            buttonRow.Controls.Add(_selectNone);

            Controls.Add(_list);
            Controls.Add(buttonRow);
        }

        /// <param name="liveValues">Current live config - shown as the "before" side of each key's
        /// value preview so the operator sees what they're actually approving, not just key names.</param>
        /// <param name="packageValues">The package's config subset - the "after" side.</param>
        public void Populate(List<ConfigDiffGroup> groups,
            IReadOnlyDictionary<string, string> liveValues, IReadOnlyDictionary<string, string> packageValues)
        {
            _groups = groups ?? new List<ConfigDiffGroup>();
            _list.Items.Clear();

            foreach (var group in _groups)
                _list.Items.Add(Describe(group, liveValues, packageValues), false);
        }

        /// <summary>All ApplicableKeys (Mismatched + PackageOnly - the keys with a package value to
        /// apply) belonging to checked groups.</summary>
        public List<string> GetSelectedKeys()
        {
            var selected = new List<string>();
            for (var i = 0; i < _groups.Count; i++)
            {
                if (_list.GetItemChecked(i))
                    selected.AddRange(_groups[i].ApplicableKeys);
            }
            return selected;
        }

        public bool HasAnyApplicableGroup => _groups.Any(g => g.ApplicableKeys.Any());

        /// <summary>Public for test visibility (checking an item via CheckedListBox's own click
        /// interaction can't be simulated headlessly) as well as the real "Select All"/"Select None"
        /// buttons - single source of truth for both.</summary>
        public void SelectAll() => SetAllChecked(true);
        public void SelectNone() => SetAllChecked(false);

        /// <summary>Test/programmatic hook to check a single row by its group index (the order
        /// Populate() was given), mirroring the real CheckedListBox click behavior.</summary>
        public void SetGroupChecked(int groupIndex, bool value) => _list.SetItemChecked(groupIndex, value);

        void SetAllChecked(bool value)
        {
            for (var i = 0; i < _list.Items.Count; i++)
                _list.SetItemChecked(i, value);
        }

        static string Describe(ConfigDiffGroup group,
            IReadOnlyDictionary<string, string> liveValues, IReadOnlyDictionary<string, string> packageValues)
        {
            if (!group.ApplicableKeys.Any())
                return $"(info only, not applied) {string.Join(", ", group.LiveOnlyKeys)}";

            var parts = new List<string>();
            foreach (var key in group.MismatchedKeys)
                parts.Add($"{key} '{ConfigValueDisplay.Preview(liveValues, key)}' -> '{ConfigValueDisplay.Preview(packageValues, key)}'");
            foreach (var key in group.PackageOnlyKeys)
                parts.Add($"{key} (new) = '{ConfigValueDisplay.Preview(packageValues, key)}'");

            return string.Join(";  ", parts);
        }
    }
}
