using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.UI
{
    /// <summary>
    /// Read-only compare report (WP2 Phase B, task B3) - a report view over the already-built
    /// ConfigCompareEngine/BsaConfigComposition.DiffImport, no apply path at all. Never mutates
    /// Settings. A sortable/filterable grid rather than a flat comma-joined text dump, since a
    /// real-world diff can span dozens of keys at once - see CompareRow for the per-row model shared
    /// with the report-building logic below.
    /// </summary>
    public class ComparePackageForm : Form
    {
        readonly TableLayoutPanel _infoTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(8, 6, 8, 2)
        };
        readonly Label _lblPackageValue = new Label { AutoSize = true, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1) };
        readonly Label _lblVersionValue = new Label { AutoSize = true, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1) };
        readonly Label _lblCreatedValue = new Label { AutoSize = true, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1) };

        // Full-width status banner (MATCH / has-differences / error) - same colour convention as
        // PreflightSignOffPanel's result banner (Green+White / Orange+Black / Red+White).
        readonly Label _lblSummary = new Label
        {
            Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0), Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold)
        };

        readonly TableLayoutPanel _filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 6, RowCount = 1, Padding = new Padding(8, 9, 8, 5)
        };
        readonly TextBox _txtSearch = new TextBox { Width = 200, Anchor = AnchorStyles.Left };
        readonly ComboBox _cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left };

        readonly DataGridView _grid = new DataGridView();

        List<CompareRow> _allRows = new List<CompareRow>();

        public ComparePackageForm(string packagePath)
        {
            Text = "Compare Current Config to Package";
            Width = 760;
            Height = 560;
            MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterParent;

            BuildLayout();
            LoadReport(packagePath);
        }

        void BuildLayout()
        {
            BuildInfoTable();
            BuildFilterRow();
            BuildGrid();

            // Dock=Top controls stack with the LAST-added one on top (see PreflightSignOffPanel's
            // layout-order comment for this codebase's convention) - so add bottom-to-top: grid fills
            // whatever remains, then filter row, then the status banner, then the info table on top.
            Controls.Add(_grid);
            Controls.Add(_filterRow);
            Controls.Add(_lblSummary);
            Controls.Add(_infoTable);
        }

        /// <summary>Package metadata as a proper label/value grid (Package / Version / Created) rather
        /// than one label with manually \n-joined lines - each field reads on its own row with a bold
        /// caption, and long values (e.g. a long filename) ellipsize instead of clipping.</summary>
        void BuildInfoTable()
        {
            _infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 3; i++)
                _infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _infoTable.Controls.Add(CaptionLabel("Package:"), 0, 0);
            _infoTable.Controls.Add(_lblPackageValue, 1, 0);
            _infoTable.Controls.Add(CaptionLabel("Version:"), 0, 1);
            _infoTable.Controls.Add(_lblVersionValue, 1, 1);
            _infoTable.Controls.Add(CaptionLabel("Created:"), 0, 2);
            _infoTable.Controls.Add(_lblCreatedValue, 1, 2);
        }

        Label CaptionLabel(string text) => new Label
        {
            Text = text, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 1, 12, 1)
        };

        /// <summary>TableLayoutPanel, not FlowLayoutPanel: a FlowLayoutPanel top-aligns every child
        /// within its flow row, so a Label (short) next to a TextBox/ComboBox (taller) drift out of
        /// vertical alignment unless margins are hand-tuned per control. A TableLayoutPanel row
        /// vertically CENTERS any child whose Anchor excludes Top/Bottom (the TextBox/ComboBox here),
        /// and Dock=Fill + TextAlign=MiddleLeft centers a Label the same way - so every control in the
        /// row lines up on one visual centerline regardless of its native height.</summary>
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
            var lblStatus = new Label { Text = "Status:", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            _cmbStatus.Items.AddRange(new object[] { "All", "Changed", "Only in package", "Only on this machine" });
            _cmbStatus.SelectedIndex = 0;

            _filterRow.Controls.Add(lblSearch, 0, 0);
            _filterRow.Controls.Add(_txtSearch, 1, 0);
            // Explicit tiny size, not a default new Panel(): WinForms' default Panel size is 200x100,
            // and an unset spacer's 100px height was silently driving the whole row's AutoSize height -
            // the actual source of the oversized gap, not the surrounding Padding/Margin values.
            _filterRow.Controls.Add(new Panel { Height = 1, Margin = Padding.Empty }, 2, 0);
            _filterRow.Controls.Add(lblStatus, 3, 0);
            _filterRow.Controls.Add(_cmbStatus, 4, 0);
            // column 5 (Percent 100, no control) absorbs remaining width so the row stays left-packed

            _txtSearch.TextChanged += (s, e) => ApplyFilter();
            _cmbStatus.SelectedIndexChanged += (s, e) => ApplyFilter();
        }

        void BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.EditMode = DataGridViewEditMode.EditProgrammatically;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 18, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Setting", HeaderText = "Setting", FillWeight = 28, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Current", HeaderText = "Current value", FillWeight = 27, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Package", HeaderText = "Package value", FillWeight = 27, SortMode = DataGridViewColumnSortMode.Automatic });
        }

        void LoadReport(string packagePath)
        {
            try
            {
                var package = BsaConfigPackage.Read(packagePath);
                var groups = BsaConfigComposition.DiffImport(package);
                _allRows = CompareRow.FromGroups(groups, BsaConfigComposition.LiveConfigView(), package.ConfigSubset);

                _lblPackageValue.Text = Path.GetFileName(packagePath);
                _lblVersionValue.Text = package.Manifest.Version;
                _lblCreatedValue.Text = $"{package.Manifest.CreatedAtUtc:u} by {package.Manifest.CreatedByOperator}";

                UpdateSummary();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _infoTable.Visible = false;
                _filterRow.Visible = false;
                _grid.Visible = false;
                _lblSummary.Text = "  Could not compare this package: " + ex.Message;
                _lblSummary.BackColor = Color.Red;
                _lblSummary.ForeColor = Color.White;
            }
        }

        void UpdateSummary()
        {
            if (_allRows.Count == 0)
            {
                _lblSummary.Text = "  MATCH - live config is identical to this package (portable settings).";
                _lblSummary.BackColor = Color.Green;
                _lblSummary.ForeColor = Color.White;
                return;
            }

            var changed = _allRows.Count(r => r.Status == CompareRowStatus.Changed);
            var onlyInPackage = _allRows.Count(r => r.Status == CompareRowStatus.OnlyInPackage);
            var onlyLocal = _allRows.Count(r => r.Status == CompareRowStatus.OnlyOnThisMachine);

            _lblSummary.Text = $"  {changed} changed · {onlyInPackage} only in package · {onlyLocal} only on this machine";
            _lblSummary.BackColor = Color.FromArgb(255, 152, 0); // orange - "differences found, review before trusting this machine's config"
            _lblSummary.ForeColor = Color.Black;
        }

        void ApplyFilter()
        {
            var search = _txtSearch.Text?.Trim() ?? "";
            var statusFilterIndex = _cmbStatus.SelectedIndex;

            var filtered = _allRows.Where(r =>
                (search.Length == 0 || r.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                StatusMatchesFilter(r.Status, statusFilterIndex));

            _grid.Rows.Clear();
            foreach (var row in filtered)
            {
                var index = _grid.Rows.Add(
                    StatusLabel(row.Status),
                    row.Key,
                    ConfigValueDisplay.Preview(row.CurrentValue),
                    ConfigValueDisplay.Preview(row.PackageValue));

                var gridRow = _grid.Rows[index];
                var style = StatusStyle(row.Status);
                gridRow.DefaultCellStyle.BackColor = style.Back;
                gridRow.DefaultCellStyle.ForeColor = style.Fore;
                gridRow.DefaultCellStyle.SelectionBackColor = style.SelectionBack;
                gridRow.DefaultCellStyle.SelectionForeColor = style.SelectionFore;
                gridRow.Cells["Current"].ToolTipText = row.CurrentValue;
                gridRow.Cells["Package"].ToolTipText = row.PackageValue;
            }
        }

        static bool StatusMatchesFilter(CompareRowStatus status, int filterIndex)
        {
            switch (filterIndex)
            {
                case 1: return status == CompareRowStatus.Changed;
                case 2: return status == CompareRowStatus.OnlyInPackage;
                case 3: return status == CompareRowStatus.OnlyOnThisMachine;
                default: return true;
            }
        }

        static string StatusLabel(CompareRowStatus status)
        {
            switch (status)
            {
                case CompareRowStatus.Changed: return "Changed";
                case CompareRowStatus.OnlyInPackage: return "Only in package";
                case CompareRowStatus.OnlyOnThisMachine: return "Only on this machine";
                default: return status.ToString();
            }
        }

        /// <summary>Soft pastel row tint + a matching dark foreground, rather than a flat saturated
        /// fill (the old Color.Khaki read poorly with default black text) - a low-contrast background
        /// with high-contrast text reads better across a whole grid than a loud one. SelectionBack/Fore
        /// use a deeper shade of the SAME hue rather than the system's default blue highlight, so
        /// selecting a row feels like a natural "darken", not a jarring color swap.</summary>
        static (Color Back, Color Fore, Color SelectionBack, Color SelectionFore) StatusStyle(CompareRowStatus status)
        {
            switch (status)
            {
                case CompareRowStatus.Changed:
                    // Amber family - "this value differs, take a look."
                    return (Color.FromArgb(255, 248, 225), Color.FromArgb(133, 100, 4),
                            Color.FromArgb(255, 193, 7), Color.Black);
                case CompareRowStatus.OnlyInPackage:
                    // Blue family - "new setting the package has that this machine doesn't."
                    return (Color.FromArgb(227, 242, 253), Color.FromArgb(13, 71, 161),
                            Color.FromArgb(33, 150, 243), Color.White);
                case CompareRowStatus.OnlyOnThisMachine:
                    // Neutral gray - informational only, nothing to apply.
                    return (Color.FromArgb(245, 245, 245), Color.FromArgb(66, 66, 66),
                            Color.FromArgb(158, 158, 158), Color.White);
                default:
                    return (Color.White, Color.Black, SystemColors.Highlight, SystemColors.HighlightText);
            }
        }
    }
}
