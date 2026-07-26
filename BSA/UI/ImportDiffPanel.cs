using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Per-key diff preview with checkboxes (source-doc requirement: "import must never blindly
    /// overwrite"). One row per KEY (not per ConfigDiffGroup) so values never get comma-joined into a
    /// hard-to-read cell - but a coupled pair like guided_alt/guided_alt_frame still applies as one
    /// unit, so their rows are visually banded together and checking either one's checkbox checks both
    /// (see RowState.Group / RefreshGrid's banding). LiveOnly-only keys (nothing in the package to
    /// apply) get their own informational row with a locked checkbox. All rows start unchecked; the
    /// operator opts in explicitly. Grid conventions (search box, status filter, pastel status colour)
    /// mirror ComparePackageForm/CompareRow, but sorting is disabled here - free column sort would
    /// scatter a coupled group's rows apart and defeat the banding.
    /// </summary>
    public class ImportDiffPanel : Panel
    {
        enum ImportRowStatus { Changed, NewInPackage, InfoOnly }

        class RowState
        {
            public ConfigDiffGroup Group;
            public string Key;
            public bool Applicable;
            public bool Checked;
            public ImportRowStatus Status;
            public string CurrentPreview;
            public string NewPreview;
            public string CurrentRaw;
            public string NewRaw;
            public Color? BandColor;
        }

        // Alternate between two very subtle tints so two coupled groups sitting back-to-back in the
        // list still read as two separate bands rather than blurring into one. Single-key groups get
        // no tint at all (SystemColors.Window) - banding signals "these rows are linked", not status.
        static readonly Color BandColorA = Color.FromArgb(237, 240, 250);
        static readonly Color BandColorB = Color.FromArgb(240, 248, 240);

        // ThemeManager (dark theme) sets a grid-wide white RowsDefaultCellStyle.ForeColor on every
        // DataGridView it finds - our light pastel row backgrounds need an explicit dark foreground
        // per row to override that, or the text is unreadable (white-on-light-gray). Per-row
        // DefaultCellStyle always wins over the grid-wide default in DataGridView's style cascade.
        static readonly Color RowTextColor = Color.FromArgb(33, 33, 33);

        readonly Label _lblSummary = new Label
        {
            Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0), Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold)
        };

        readonly TableLayoutPanel _filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 6, RowCount = 1, Padding = new Padding(8, 6, 8, 5)
        };
        readonly TextBox _txtSearch = new TextBox { Width = 200, Anchor = AnchorStyles.Left };
        readonly ComboBox _cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left };

        readonly DataGridView _grid = new DataGridView();

        readonly Button _selectAll = new Button { Text = "Select All", AutoSize = true };
        readonly Button _selectNone = new Button { Text = "Select None", AutoSize = true };

        List<ConfigDiffGroup> _groups = new List<ConfigDiffGroup>();
        List<RowState> _rows = new List<RowState>();
        List<RowState> _visibleRows = new List<RowState>();
        bool _suppressEvents;

        public ImportDiffPanel()
        {
            Dock = DockStyle.Fill;

            BuildFilterRow();
            BuildGrid();

            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            _selectAll.Click += (s, e) => SelectAll();
            _selectNone.Click += (s, e) => SelectNone();
            buttonRow.Controls.Add(_selectAll);
            buttonRow.Controls.Add(_selectNone);

            // Dock=Top controls stack with the LAST-added one on top (same convention as
            // ComparePackageForm) - add bottom-to-top: grid fills whatever remains, buttonRow pins to
            // the bottom edge, then the filter row, then the summary line ends up topmost.
            Controls.Add(_grid);
            Controls.Add(buttonRow);
            Controls.Add(_filterRow);
            Controls.Add(_lblSummary);
        }

        void BuildFilterRow()
        {
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblSearch = new Label { Text = "Search:", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var lblStatus = new Label { Text = "Show:", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            _cmbStatus.Items.AddRange(new object[] { "All", "Changed", "New in package", "Info only" });
            _cmbStatus.SelectedIndex = 0;

            _filterRow.Controls.Add(lblSearch, 0, 0);
            _filterRow.Controls.Add(_txtSearch, 1, 0);
            _filterRow.Controls.Add(new Panel { Height = 1, Margin = Padding.Empty }, 2, 0);
            _filterRow.Controls.Add(lblStatus, 3, 0);
            _filterRow.Controls.Add(_cmbStatus, 4, 0);

            _txtSearch.TextChanged += (s, e) => RefreshGrid();
            _cmbStatus.SelectedIndexChanged += (s, e) => RefreshGrid();
        }

        void BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;

            var colCheck = new DataGridViewCheckBoxColumn
            {
                Name = "Apply", HeaderText = "Apply",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 50,
                Resizable = DataGridViewTriState.False
            };
            _grid.Columns.Add(colCheck);

            // NotSortable, unlike ComparePackageForm's grid - a free column sort would scatter a
            // coupled group's rows apart and defeat the band grouping below.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Setting", HeaderText = "Setting", FillWeight = 30, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Current", HeaderText = "Current value", FillWeight = 22, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "New", HeaderText = "New value", FillWeight = 22, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 20, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });

            // Standard WinForms idiom for single-click checkbox toggling: commit the edit as soon as
            // the cell goes dirty instead of waiting for focus to leave the cell.
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
        }

        /// <param name="liveValues">Current live config - shown as the "before" side of each key's
        /// value preview so the operator sees what they're actually approving, not just key names.</param>
        /// <param name="packageValues">The package's config subset - the "after" side.</param>
        public void Populate(List<ConfigDiffGroup> groups,
            IReadOnlyDictionary<string, string> liveValues, IReadOnlyDictionary<string, string> packageValues)
        {
            _groups = groups ?? new List<ConfigDiffGroup>();

            var flatRows = new List<RowState>();
            var useAltBand = false;
            foreach (var group in _groups)
            {
                var groupRows = BuildRows(group, liveValues, packageValues);

                // Only groups that actually produce more than one row need a band - a lone row has
                // nothing to visually link to.
                if (groupRows.Count > 1)
                {
                    var band = useAltBand ? BandColorB : BandColorA;
                    useAltBand = !useAltBand;
                    foreach (var row in groupRows)
                        row.BandColor = band;
                }

                flatRows.AddRange(groupRows);
            }
            _rows = flatRows;

            _suppressEvents = true;
            _txtSearch.Text = "";
            _cmbStatus.SelectedIndex = 0;
            _suppressEvents = false;

            RefreshGrid();
        }

        static List<RowState> BuildRows(ConfigDiffGroup group,
            IReadOnlyDictionary<string, string> liveValues, IReadOnlyDictionary<string, string> packageValues)
        {
            var rows = new List<RowState>();

            foreach (var key in group.MismatchedKeys)
                rows.Add(new RowState
                {
                    Group = group, Key = key, Applicable = true, Status = ImportRowStatus.Changed,
                    CurrentPreview = ConfigValueDisplay.Preview(liveValues, key),
                    NewPreview = ConfigValueDisplay.Preview(packageValues, key),
                    CurrentRaw = ValueOrEmpty(liveValues, key),
                    NewRaw = ValueOrEmpty(packageValues, key)
                });

            foreach (var key in group.PackageOnlyKeys)
                rows.Add(new RowState
                {
                    Group = group, Key = key, Applicable = true, Status = ImportRowStatus.NewInPackage,
                    CurrentPreview = "", NewPreview = ConfigValueDisplay.Preview(packageValues, key),
                    CurrentRaw = "", NewRaw = ValueOrEmpty(packageValues, key)
                });

            // Shown even when the same group also has applicable keys (previously these were silently
            // dropped entirely when a group had anything applicable - see the old Describe() method) -
            // every key the operator's live config has now gets its own visible row.
            foreach (var key in group.LiveOnlyKeys)
                rows.Add(new RowState
                {
                    Group = group, Key = key, Applicable = false, Status = ImportRowStatus.InfoOnly,
                    CurrentPreview = ConfigValueDisplay.Preview(liveValues, key), NewPreview = "",
                    CurrentRaw = ValueOrEmpty(liveValues, key), NewRaw = ""
                });

            return rows;
        }

        static string ValueOrEmpty(IReadOnlyDictionary<string, string> values, string key) =>
            values != null && values.TryGetValue(key, out var value) && value != null ? value : "";

        void RefreshGrid()
        {
            var search = _txtSearch.Text?.Trim() ?? "";
            var filterIndex = _cmbStatus.SelectedIndex;

            _visibleRows = _rows.Where(r =>
                (search.Length == 0 || r.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                StatusMatchesFilter(r.Status, filterIndex)).ToList();

            _suppressEvents = true;
            _grid.Rows.Clear();
            foreach (var row in _visibleRows)
            {
                var index = _grid.Rows.Add(row.Checked, row.Key, row.CurrentPreview, row.NewPreview, StatusLabel(row.Status));
                var gridRow = _grid.Rows[index];
                gridRow.Tag = row;

                gridRow.DefaultCellStyle.BackColor = row.BandColor ?? SystemColors.Window;
                gridRow.DefaultCellStyle.ForeColor = RowTextColor;
                gridRow.Cells["Current"].ToolTipText = row.CurrentRaw;
                gridRow.Cells["New"].ToolTipText = row.NewRaw;
                gridRow.Cells["Status"].Style.ForeColor = StatusColor(row.Status);

                if (!row.Applicable)
                {
                    // Nothing in the package to apply for this key - lock the checkbox rather than
                    // leaving it clickable-but-meaningless.
                    gridRow.Cells["Apply"].ReadOnly = true;
                }
                else
                {
                    var siblingKeys = row.Group.ApplicableKeys.Where(k => k != row.Key).ToList();
                    if (siblingKeys.Count > 0)
                        gridRow.Cells["Apply"].ToolTipText = "Applies together with: " + string.Join(", ", siblingKeys);
                }
            }
            _suppressEvents = false;

            UpdateSummary();
        }

        void UpdateSummary()
        {
            var applicable = _rows.Count(r => r.Applicable);
            var infoOnly = _rows.Count - applicable;
            var selected = _rows.Count(r => r.Applicable && r.Checked);

            _lblSummary.Text = $"  {selected} of {applicable} setting(s) selected to apply" +
                                (infoOnly > 0 ? $" · {infoOnly} informational" : "");
        }

        void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressEvents || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Apply")
                return;

            if (!(_grid.Rows[e.RowIndex].Tag is RowState rowState))
                return;

            var newValue = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as bool? == true;

            // Coupled keys must move together as one unit (never half-applied) - sync every applicable
            // row of the same group, not just the one the operator clicked, including any row not
            // currently visible under the active filter.
            _suppressEvents = true;
            foreach (var sibling in _rows.Where(r => r.Applicable && r.Group == rowState.Group))
                sibling.Checked = newValue;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.Tag is RowState rs && rs.Applicable && rs.Group == rowState.Group)
                    gridRow.Cells["Apply"].Value = newValue;
            }
            _suppressEvents = false;

            UpdateSummary();
        }

        static bool StatusMatchesFilter(ImportRowStatus status, int filterIndex)
        {
            switch (filterIndex)
            {
                case 1: return status == ImportRowStatus.Changed;
                case 2: return status == ImportRowStatus.NewInPackage;
                case 3: return status == ImportRowStatus.InfoOnly;
                default: return true;
            }
        }

        static string StatusLabel(ImportRowStatus status)
        {
            switch (status)
            {
                case ImportRowStatus.Changed: return "Changed";
                case ImportRowStatus.NewInPackage: return "New in package";
                case ImportRowStatus.InfoOnly: return "Info only";
                default: return status.ToString();
            }
        }

        /// <summary>Same status vocabulary/colour family as CompareRowStatus (Changed=amber,
        /// NewInPackage~OnlyInPackage=blue, InfoOnly~OnlyOnThisMachine=gray) - only the Status cell's
        /// text is coloured, not the whole row, since the row background is reserved for group
        /// banding.</summary>
        static Color StatusColor(ImportRowStatus status)
        {
            switch (status)
            {
                case ImportRowStatus.Changed: return Color.FromArgb(133, 100, 4);
                case ImportRowStatus.NewInPackage: return Color.FromArgb(13, 71, 161);
                case ImportRowStatus.InfoOnly: return Color.FromArgb(117, 117, 117);
                default: return SystemColors.ControlText;
            }
        }

        /// <summary>All ApplicableKeys (Mismatched + PackageOnly - the keys with a package value to
        /// apply) belonging to groups with at least one checked row. Checked state is always kept in
        /// sync across every applicable row of a group (see Grid_CellValueChanged/SetVisibleChecked/
        /// SetGroupChecked), so this never returns a partial group.</summary>
        public List<string> GetSelectedKeys()
        {
            var selected = new List<string>();
            foreach (var group in _rows.Where(r => r.Applicable && r.Checked).Select(r => r.Group).Distinct())
                selected.AddRange(group.ApplicableKeys);
            return selected;
        }

        public bool HasAnyApplicableGroup => _rows.Any(r => r.Applicable);

        /// <summary>Operates on the currently filtered/visible rows only, not every group - so
        /// narrowing the search/status filter and then clicking Select All can't silently check hidden
        /// groups the operator never looked at. Any group with at least one visible applicable row gets
        /// checked as a whole (including its non-visible sibling rows, if any) - a coupled group is
        /// still never half-applied. Public for test visibility (checking an item via DataGridView's
        /// own click interaction can't be simulated headlessly) as well as the real "Select All"/
        /// "Select None" buttons - single source of truth for both.</summary>
        public void SelectAll() => SetVisibleChecked(true);
        public void SelectNone() => SetVisibleChecked(false);

        void SetVisibleChecked(bool value)
        {
            var touchedGroups = new HashSet<ConfigDiffGroup>(_visibleRows.Where(r => r.Applicable).Select(r => r.Group));

            foreach (var row in _rows.Where(r => r.Applicable && touchedGroups.Contains(r.Group)))
                row.Checked = value;

            RefreshGrid();
        }

        /// <summary>Test/programmatic hook to check every applicable row of a single group by its
        /// index in the list Populate() was given, mirroring the real grid checkbox click behavior.
        /// Indexes into the full unfiltered group list, unlike SelectAll/SelectNone.</summary>
        public void SetGroupChecked(int groupIndex, bool value)
        {
            var group = _groups[groupIndex];
            foreach (var row in _rows.Where(r => r.Applicable && r.Group == group))
                row.Checked = value;
            RefreshGrid();
        }

        /// <summary>Test hook mirroring the real search box - setting this filters which rows are
        /// shown (and therefore which rows SelectAll/SelectNone act on), exactly as typing in the box
        /// would. Headless tests can't simulate typing into a live TextBox's UI.</summary>
        public string SearchFilter
        {
            get => _txtSearch.Text;
            set => _txtSearch.Text = value ?? "";
        }

        /// <summary>Test hook mirroring the real status combo (0=All, 1=Changed, 2=New in package,
        /// 3=Info only).</summary>
        public int StatusFilter
        {
            get => _cmbStatus.SelectedIndex;
            set => _cmbStatus.SelectedIndex = value;
        }
    }
}
