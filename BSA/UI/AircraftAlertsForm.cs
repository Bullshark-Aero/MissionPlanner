using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using MissionPlanner.BSA.Alerts;
using MissionPlanner.BSA.Config;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.UI
{
    public class AircraftAlertsForm : Form
    {
        public const string Caption = "Aircraft Alerts";

        sealed class Choice
        {
            public string Text;
            public object Value;
            public override string ToString() => Text;
        }

        static readonly Choice[] PhaseChoices =
        {
            new Choice { Text = "Any time", Value = AlertPhase.Any },
            new Choice { Text = "Armed", Value = AlertPhase.Armed },
            new Choice { Text = "Airborne, including hover", Value = AlertPhase.Airborne },
            new Choice { Text = "Hover", Value = AlertPhase.Hover },
            new Choice { Text = "Transition and forward flight", Value = AlertPhase.Forward }
        };

        static readonly Choice[] KindChoices =
        {
            new Choice { Text = "Alarm", Value = AlertKind.Alarm },
            new Choice { Text = "Readout", Value = AlertKind.Readout }
        };

        const string TriggerAlways = "always";
        const string TriggerOnStale = "stale";

        static readonly Choice[] TriggerChoices =
        {
            ConditionChoice(CustomWarning.Conditional.LT),
            ConditionChoice(CustomWarning.Conditional.LTEQ),
            ConditionChoice(CustomWarning.Conditional.EQ),
            ConditionChoice(CustomWarning.Conditional.NEQ),
            ConditionChoice(CustomWarning.Conditional.GTEQ),
            ConditionChoice(CustomWarning.Conditional.GT),
            new Choice { Text = "always", Value = TriggerAlways },
            new Choice { Text = "when the data stops", Value = TriggerOnStale }
        };

        static readonly Choice[] UnknownChoices =
        {
            new Choice { Text = "Alert anyway (armed)", Value = AlertPhaseUnknown.AlertAnyway },
            new Choice { Text = "Stay quiet", Value = AlertPhaseUnknown.StayQuiet }
        };

        static readonly Choice[] PreflightChoices =
        {
            new Choice { Text = "Not a pre-flight check", Value = AlertPreflight.None },
            new Choice { Text = "Pre-flight warning", Value = AlertPreflight.Warning },
            new Choice { Text = "Pre-flight critical", Value = AlertPreflight.Critical }
        };

        readonly List<CustomWarning> _alerts;
        readonly Label _phaseChip = Chip();
        readonly Label _namedChip = Chip();
        readonly Label _speechChip = Chip();
        readonly ListView _list = new ListView
        {
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill
        };

        readonly Button _addButton = new Button { Text = "Add alert", AutoSize = true };
        readonly Button _presetButton = new Button { Text = "Add from preset...", AutoSize = true };
        readonly Button _sayButton = new Button { Text = "Say it now", AutoSize = true };
        readonly Button _deleteButton = new Button { Text = "Delete", AutoSize = true };
        readonly Button _saveButton = new Button { Text = "Save", AutoSize = true };
        readonly Button _closeButton = new Button { Text = "Close", AutoSize = true };
        readonly Label _saveState = new Label { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
        readonly ContextMenuStrip _presetMenu = new ContextMenuStrip();

        readonly TextBox _title = Box(220);
        readonly ComboBox _kind = Combo(KindChoices, 110);
        readonly ComboBox _watch = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
        readonly ComboBox _phase = Combo(PhaseChoices, 200);
        readonly ComboBox _trigger = Combo(TriggerChoices, 130);
        readonly TextBox _threshold = Box(70);
        readonly TextBox _hold = Box(45);
        readonly ComboBox _unknown = Combo(UnknownChoices, 200);
        readonly TextBox _fresh = Box(45);
        readonly TextBox _staleText = Box(200);
        readonly TextBox _say = Box(220);
        readonly TextBox _repeat = Box(45);
        readonly TextBox _grace = Box(45);
        readonly CheckBox _show = new CheckBox { Text = "Colour its quick view", AutoSize = true };
        readonly ComboBox _colour = new ComboBox
        {
            Width = 130,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        readonly ComboBox _preflight = Combo(PreflightChoices, 200);
        readonly Panel _editor = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly Timer _statusTimer = new Timer { Interval = 1000 };

        bool _loading;
        CustomWarning _selected;
        string _savedState;
        string _lastColour = AlertColours.DefaultColour;
        DateTime? _savedAt;

        public AircraftAlertsForm(IEnumerable<CustomWarning> current)
        {
            _alerts = (current ?? Enumerable.Empty<CustomWarning>()).Where(w => w?.Alert != null).Select(Clone).ToList();
            _savedState = Snapshot(_alerts);

            Text = Caption;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1060, 760);
            MinimumSize = new Size(820, 600);
            ShowInTaskbar = false;

            _list.Columns.Add("Alert", 170);
            _list.Columns.Add("Watches", 150);
            _list.Columns.Add("When", 170);
            _list.Columns.Add("Trigger", 170);
            _list.Columns.Add("Says", 150);
            _list.Columns.Add("Every", 60);
            _list.Columns.Add("Pre-flight", 90);

            foreach (var name in AlertColours.Named) _colour.Items.Add(name);
            _colour.Items.Add(AlertColours.CustomChoice);
            _colour.DrawItem += DrawColourItem;
            PickColour = start =>
            {
                using (var dialog = new ColorDialog { Color = start, FullOpen = true, AnyColor = true })
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Color : (Color?)null;
            };
            foreach (var option in WatchOptions()) _watch.Items.Add(option);
            foreach (var id in AlertPresets.Ids)
            {
                var preset = AlertPresets.Create(id);
                var item = new ToolStripMenuItem(preset.Alert.Title) { Tag = id };
                item.Click += (s, e) => AddPreset((string)((ToolStripMenuItem)s).Tag);
                _presetMenu.Items.Add(item);
            }

            var chips = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
            chips.Controls.Add(_phaseChip);
            chips.Controls.Add(_namedChip);
            chips.Controls.Add(_speechChip);

            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
            actions.Controls.AddRange(new Control[] { _addButton, _presetButton, _sayButton, _deleteButton });

            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            bottom.Controls.Add(_closeButton);
            bottom.Controls.Add(_saveButton);
            bottom.Controls.Add(_saveState);

            BuildEditor();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(chips, 0, 0);
            layout.Controls.Add(_list, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            layout.Controls.Add(_editor, 0, 3);
            layout.Controls.Add(bottom, 0, 4);
            Controls.Add(layout);

            _addButton.Click += (s, e) => AddAlert();
            _presetButton.Click += (s, e) => _presetMenu.Show(_presetButton, new Point(0, _presetButton.Height));
            _sayButton.Click += (s, e) => SayNow();
            _deleteButton.Click += (s, e) => DeleteSelected();
            _saveButton.Click += (s, e) => Save();
            _closeButton.Click += (s, e) => Close();
            _list.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            _list.ItemChecked += OnItemChecked;
            _statusTimer.Tick += (s, e) =>
            {
                RefreshStatus();
                UpdateSaveState();
            };
            HookEditorEvents();

            RefreshList(_alerts.FirstOrDefault());
            UpdateStatusChips(false, false, 0, Judicar2600BundleProfile.NamedFields.Length, false);
        }

        public IReadOnlyList<CustomWarning> Alerts => _alerts;

        public Func<Color, Color?> PickColour { get; set; }

        public string SelectedColour => _colour.SelectedItem as string;

        public bool Dirty => Snapshot(_alerts) != _savedState;

        public CustomWarning Selected => _selected;

        public string PhaseStatus => _phaseChip.Text;
        public string NamedStatus => _namedChip.Text;
        public string SpeechStatus => _speechChip.Text;
        public string SaveStatus => _saveState.Text;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                Utilities.ThemeManager.ApplyThemeTo(this);
            }
            catch
            {
            }
            RefreshStatus();
            UpdateSaveState();
            _statusTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Dirty && e.CloseReason == CloseReason.UserClosing &&
                CustomMessageBox.Show("The aircraft alerts have changes that are not saved.\n\nClose without saving them?",
                    Caption, CustomMessageBox.MessageBoxButtons.YesNo) != CustomMessageBox.DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _statusTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusTimer.Dispose();
                _presetMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        public CustomWarning AddAlert()
        {
            var alert = new CustomWarning
            {
                Text = "",
                RepeatTime = 10,
                ConditionType = CustomWarning.Conditional.LT,
                color = "Red",
                Alert = new AlertOptions
                {
                    Id = AlertPresets.UniqueId("alert", _alerts),
                    Title = "New alert",
                    Kind = AlertKind.Alarm,
                    Source = AlertSource.Field("rpm1"),
                    Phase = AlertPhase.Armed,
                    WhenPhaseUnknown = FlightPhase.DefaultUnknownRule(AlertKind.Alarm)
                }
            };
            Add(alert);
            return alert;
        }

        public CustomWarning AddPreset(string presetId)
        {
            var alert = AlertPresets.Create(presetId);
            alert.Alert.Id = AlertPresets.UniqueId(alert.Alert.Id, _alerts);
            Add(alert);
            return alert;
        }

        public void DeleteSelected()
        {
            var selected = Selected;
            if (selected == null) return;
            var index = _alerts.IndexOf(selected);
            _alerts.Remove(selected);
            RefreshList(_alerts.Count == 0 ? null : _alerts[Math.Min(index, _alerts.Count - 1)]);
        }

        public bool Save()
        {
            var problems = Validate(_alerts);
            if (problems.Count > 0)
            {
                CustomMessageBox.Show("The alerts were not saved:\n\n" + string.Join("\n", problems), Caption);
                return false;
            }
            if (!LockGateUi.AllowedToProceed("mp_setting_change", "aircraft_alerts", "Changing the aircraft alerts"))
                return false;
            try
            {
                ApplyAlerts(WarningEngine.warnings, _alerts);
                WarningEngine.SaveConfig();
                _savedState = Snapshot(_alerts);
                _savedAt = DateTime.Now;
                UpdateSaveState();
                return true;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("The alerts could not be saved: " + ex.Message, Caption);
                return false;
            }
        }

        public static void ApplyAlerts(List<CustomWarning> shared, IEnumerable<CustomWarning> alerts)
        {
            var copies = (alerts ?? Enumerable.Empty<CustomWarning>()).Select(Clone).ToList();
            lock (shared)
            {
                shared.RemoveAll(w => w?.Alert != null);
                shared.AddRange(copies);
            }
        }

        static string Snapshot(IEnumerable<CustomWarning> alerts)
        {
            var serializer = new XmlSerializer(typeof(List<CustomWarning>), new[] { typeof(CustomWarning) });
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, alerts.ToList());
                return writer.ToString();
            }
        }

        public static CustomWarning Clone(CustomWarning warning)
        {
            var serializer = new XmlSerializer(typeof(CustomWarning));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, warning);
                using (var reader = new StringReader(writer.ToString()))
                    return (CustomWarning)serializer.Deserialize(reader);
            }
        }

        public static List<string> Validate(IEnumerable<CustomWarning> alerts)
        {
            var problems = new List<string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var warning in alerts ?? Enumerable.Empty<CustomWarning>())
            {
                var alert = warning?.Alert;
                if (alert == null) continue;
                var name = string.IsNullOrWhiteSpace(alert.Title) ? alert.Id : alert.Title;
                if (string.IsNullOrWhiteSpace(alert.Id) || !ids.Add(alert.Id))
                    problems.Add(name + ": needs a unique id.");
                if (string.IsNullOrWhiteSpace(alert.Title))
                    problems.Add(name + ": needs a name.");
                if (AlertSource.Parse(alert.Source).Kind == AlertSourceKind.Unknown)
                    problems.Add(name + ": choose what it watches.");
                if (string.IsNullOrWhiteSpace(warning.Text))
                    problems.Add(name + ": needs something to say.");
                if (!alert.Always && !alert.TriggerOnStale && warning.ConditionType == CustomWarning.Conditional.NONE)
                    problems.Add(name + ": needs a trigger.");
                if (alert.HoldSeconds < 0 || alert.FreshSeconds <= 0 || alert.ArmedGraceSeconds < 0 || warning.RepeatTime < 0)
                    problems.Add(name + ": times must be positive.");
            }
            return problems;
        }

        public static string WatchLabel(AlertOptions alert) => AlertDescriptions.WatchLabel(alert);

        public static string WhenLabel(AlertOptions alert) =>
            PhaseChoices.First(c => (AlertPhase)c.Value == alert.Phase).Text;

        public static string TriggerLabel(CustomWarning warning) => AlertDescriptions.TriggerLabel(warning);

        public static string EveryLabel(CustomWarning warning) => AlertDescriptions.Seconds(AlertEvaluator.RepeatSeconds(warning));

        public static string PreflightLabel(AlertOptions alert) =>
            alert.Preflight == AlertPreflight.None ? "-" : alert.Preflight.ToString();

        public static string PreviewText(CustomWarning warning)
        {
            var alert = warning.Alert;
            var sample = alert.Kind == AlertKind.Readout ? "25" : warning.Warning.ToString("0.##", CultureInfo.InvariantCulture);
            var text = (warning.Text ?? "")
                .Replace("{value}", sample)
                .Replace("{warning}", warning.Warning.ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("{name}", alert.Title ?? "")
                .Trim();
            return text.Length > 0 ? text : (alert.StaleText ?? "").Trim();
        }

        public void UpdateStatusChips(bool linkOpen, bool phaseKnown, int namedFresh, int namedTotal, bool speechOn)
        {
            _phaseChip.Text = !linkOpen
                ? "Flight phase: no aircraft connected"
                : phaseKnown
                    ? "Flight phase: receiving"
                    : "Flight phase: not received (requested while an alert needs it)";
            _phaseChip.ForeColor = linkOpen && phaseKnown ? Color.ForestGreen : Color.DarkOrange;
            _namedChip.Text = "Judicar values: " + namedFresh + " of " + namedTotal + " fresh";
            _namedChip.ForeColor = linkOpen && namedFresh == namedTotal ? Color.ForestGreen : Color.DarkOrange;
            _speechChip.Text = speechOn ? "Speech is on" : "Speech is off in Mission Planner: alerts will only show";
            _speechChip.ForeColor = speechOn ? Color.ForestGreen : Color.DarkOrange;
        }

        void RefreshStatus()
        {
            try
            {
                var port = MainV2.comPort;
                var now = DateTime.UtcNow;
                var linkOpen = port?.BaseStream != null && port.BaseStream.IsOpen && !port.logreadmode;
                var phaseKnown = false;
                if (linkOpen)
                {
                    var packet = port.MAV.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);
                    phaseKnown = packet != null && AlertSourceResolver.IsFresh(packet.rxtime, now, FlightPhase.PhaseFreshSeconds);
                }
                var fresh = 0;
                var telemetry = AlertComposition.CurrentTelemetry();
                if (linkOpen && telemetry != null)
                {
                    fresh = (int)AlertSourceResolver.Read(new AlertOptions
                    {
                        Source = AlertSource.NamedFreshCount,
                        FreshSeconds = 5,
                        SourceNames = Judicar2600BundleProfile.NamedFields.ToList()
                    }, telemetry, now).Value;
                }
                UpdateStatusChips(linkOpen, phaseKnown, fresh, Judicar2600BundleProfile.NamedFields.Length, MainV2.speechEnabled());
            }
            catch
            {
            }
        }

        void SayNow()
        {
            var selected = Selected;
            if (selected == null) return;
            var text = PreviewText(selected);
            if (text.Length == 0) return;
            if (MainV2.speechEngine == null || !MainV2.speechEnabled())
            {
                CustomMessageBox.Show("Speech is off in Mission Planner, so nothing can be said.\n\nIt would say: " + text, Caption);
                return;
            }
            MainV2.speechEngine.SpeakAsync(text);
        }

        void Add(CustomWarning alert)
        {
            _alerts.Add(alert);
            RefreshList(alert);
        }

        void OnSelectionChanged()
        {
            if (_loading) return;
            _selected = _list.SelectedItems.Count == 0 ? null : (CustomWarning)_list.SelectedItems[0].Tag;
            LoadEditor();
        }

        void RefreshList(CustomWarning select)
        {
            _selected = select;
            _loading = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var warning in _alerts)
                {
                    var item = new ListViewItem(RowText(warning).ToArray()) { Tag = warning, Checked = warning.Alert.Enabled };
                    _list.Items.Add(item);
                    if (ReferenceEquals(warning, select)) item.Selected = true;
                }
                _list.EndUpdate();
            }
            finally
            {
                _loading = false;
            }
            LoadEditor();
            UpdateSaveState();
        }

        void UpdateSaveState()
        {
            var dirty = Dirty;
            _saveState.Text = dirty
                ? "Unsaved changes"
                : _savedAt.HasValue
                    ? "Saved at " + _savedAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "No unsaved changes";
            _saveState.ForeColor = dirty ? Color.DarkOrange : Color.ForestGreen;
        }

        static IEnumerable<string> RowText(CustomWarning warning)
        {
            var alert = warning.Alert;
            yield return alert.Title ?? alert.Id;
            yield return WatchLabel(alert);
            yield return WhenLabel(alert);
            yield return TriggerLabel(warning);
            yield return warning.Text ?? "";
            yield return EveryLabel(warning);
            yield return PreflightLabel(alert);
        }

        void RefreshSelectedRow()
        {
            var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, _selected));
            if (item == null) return;
            var values = RowText((CustomWarning)item.Tag).ToArray();
            for (var i = 0; i < values.Length; i++) item.SubItems[i].Text = values[i];
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_loading) return;
            var warning = (CustomWarning)e.Item.Tag;
            if (warning.Alert.Enabled == e.Item.Checked) return;
            warning.Alert.Enabled = e.Item.Checked;
            UpdateSaveState();
        }

        void BuildEditor()
        {
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 4, 0, 0) };
            void AddRow(string leftLabel, Control left, string rightLabel, Control right)
            {
                var row = grid.RowCount++;
                grid.Controls.Add(FieldLabel(leftLabel), 0, row);
                grid.Controls.Add(left, 1, row);
                if (rightLabel != null)
                {
                    grid.Controls.Add(FieldLabel(rightLabel), 2, row);
                    grid.Controls.Add(right, 3, row);
                }
            }

            AddRow("Name", _title, "Kind", _kind);
            AddRow("Watch", _watch, "When", _phase);
            AddRow("Trigger", Inline(_trigger, _threshold, InlineLabel("for more than"), _hold, InlineLabel("s")),
                "If phase unknown", _unknown);
            AddRow("Data counts as stopped after", Inline(_fresh, InlineLabel("s")), "If data stops, say", _staleText);
            AddRow("Say", _say, "Every", Inline(_repeat, InlineLabel("s")));
            AddRow("After arming, wait", Inline(_grace, InlineLabel("s")), "Pre-flight", _preflight);
            AddRow("Show", Inline(_show, _colour), null, null);

            var heading = new Label
            {
                Text = "Selected alert",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
                Dock = DockStyle.Top
            };
            grid.Location = new Point(0, 22);
            _editor.Controls.Add(grid);
            _editor.Controls.Add(heading);
        }

        void HookEditorEvents()
        {
            foreach (var box in new[] { _title, _threshold, _hold, _fresh, _staleText, _say, _repeat, _grace })
                box.TextChanged += (s, e) => ApplyEditor();
            foreach (var combo in new[] { _kind, _phase, _trigger, _unknown, _preflight })
                combo.SelectedIndexChanged += (s, e) => ApplyEditor();
            _colour.SelectedIndexChanged += (s, e) => OnColourChanged();
            _watch.TextChanged += (s, e) => ApplyEditor();
            _show.CheckedChanged += (s, e) => ApplyEditor();
        }

        void LoadEditor()
        {
            var warning = Selected;
            _editor.Enabled = warning != null;
            _sayButton.Enabled = _deleteButton.Enabled = warning != null;
            if (warning == null) return;
            var alert = warning.Alert;
            _loading = true;
            try
            {
                _title.Text = alert.Title ?? "";
                SelectChoice(_kind, alert.Kind);
                _watch.Text = WatchText(alert.Source);
                SelectChoice(_phase, alert.Phase);
                SelectChoice(_trigger, alert.TriggerOnStale ? TriggerOnStale : alert.Always ? (object)TriggerAlways : warning.ConditionType);
                _threshold.Text = warning.Warning.ToString("0.###", CultureInfo.InvariantCulture);
                _hold.Text = alert.HoldSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                SelectChoice(_unknown, alert.WhenPhaseUnknown);
                _fresh.Text = AlertSourceResolver.FreshSecondsOf(alert).ToString("0.###", CultureInfo.InvariantCulture);
                _staleText.Text = alert.StaleText ?? "";
                _say.Text = warning.Text ?? "";
                _repeat.Text = warning.RepeatTime.ToString(CultureInfo.InvariantCulture);
                _grace.Text = alert.ArmedGraceSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                _show.Checked = alert.Show;
                SelectColour(warning.color);
                SelectChoice(_preflight, alert.Preflight);
                foreach (var box in new[] { _threshold, _hold, _fresh, _repeat, _grace }) MarkValid(box, true);
                UpdateEditorState();
            }
            finally
            {
                _loading = false;
            }
        }

        void ApplyEditor()
        {
            if (_loading) return;
            var warning = Selected;
            if (warning == null) return;
            var alert = warning.Alert;

            alert.Title = _title.Text.Trim();
            alert.Kind = (AlertKind)Value(_kind);
            alert.Source = ParseWatch(_watch.Text, alert.Source);
            if (AlertSource.Parse(alert.Source).Kind == AlertSourceKind.NamedFreshCount &&
                (alert.SourceNames == null || alert.SourceNames.Count == 0))
                alert.SourceNames = Judicar2600BundleProfile.NamedFields.Select(AlertSource.NormalizeName).ToList();
            alert.Phase = (AlertPhase)Value(_phase);
            var trigger = Value(_trigger);
            alert.Always = Equals(trigger, TriggerAlways);
            alert.TriggerOnStale = Equals(trigger, TriggerOnStale);
            warning.ConditionType = trigger is CustomWarning.Conditional condition ? condition : CustomWarning.Conditional.NONE;
            if (TryNumber(_threshold, out var threshold)) warning.Warning = threshold;
            if (TryNumber(_hold, out var hold, 0)) alert.HoldSeconds = hold;
            alert.WhenPhaseUnknown = (AlertPhaseUnknown)Value(_unknown);
            if (TryNumber(_fresh, out var fresh, 0.1)) alert.FreshSeconds = fresh;
            alert.StaleText = _staleText.Text.Trim();
            warning.Text = _say.Text;
            if (TryNumber(_repeat, out var repeat, 0) && repeat <= int.MaxValue) warning.RepeatTime = (int)Math.Round(repeat);
            if (TryNumber(_grace, out var grace, 0)) alert.ArmedGraceSeconds = grace;
            alert.Show = _show.Checked;
            if (_colour.SelectedItem is string colour && colour != AlertColours.CustomChoice)
            {
                warning.color = colour;
                _lastColour = colour;
            }
            alert.Preflight = (AlertPreflight)Value(_preflight);

            UpdateEditorState();
            RefreshSelectedRow();
            UpdateSaveState();
        }

        void OnColourChanged()
        {
            if (_loading) return;
            if (SelectedColour == AlertColours.CustomChoice)
            {
                AlertColours.TryParse(_lastColour, out var current);
                var picked = PickColour?.Invoke(current);
                _loading = true;
                try
                {
                    SelectColour(picked.HasValue ? AlertColours.ToText(picked.Value) : _lastColour);
                }
                finally
                {
                    _loading = false;
                }
            }
            ApplyEditor();
        }

        void SelectColour(string text)
        {
            var colour = AlertColours.Normalize(text);
            if (!_colour.Items.Contains(colour)) _colour.Items.Insert(_colour.Items.Count - 1, colour);
            _colour.SelectedItem = colour;
            _lastColour = colour;
        }

        void DrawColourItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var text = _colour.Items[e.Index] as string ?? "";
            var size = Math.Max(6, e.Bounds.Height - 6);
            var swatch = new Rectangle(e.Bounds.Left + 3, e.Bounds.Top + (e.Bounds.Height - size) / 2, size, size);
            if (AlertColours.TryParse(text, out var colour))
            {
                using (var brush = new SolidBrush(colour)) e.Graphics.FillRectangle(brush, swatch);
                e.Graphics.DrawRectangle(SystemPens.ControlDark, swatch);
            }
            var textBounds = new Rectangle(swatch.Right + 5, e.Bounds.Top, Math.Max(0, e.Bounds.Right - swatch.Right - 5), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, e.Font, textBounds, e.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        void UpdateEditorState()
        {
            var trigger = Value(_trigger);
            var compares = trigger is CustomWarning.Conditional;
            _threshold.Enabled = compares;
            _hold.Enabled = !Equals(trigger, TriggerOnStale);
            _unknown.Enabled = FlightPhase.NeedsPhase((AlertPhase)Value(_phase));
            _colour.Enabled = _show.Checked;
        }

        static IEnumerable<string> WatchOptions()
        {
            yield return WatchLabel(new AlertOptions { Source = AlertSource.GpsFixCount });
            yield return "Fresh Judicar values";
            foreach (var field in Judicar2600BundleProfile.NamedFields)
                yield return WatchLabel(new AlertOptions { Source = AlertSource.Named(field) });
            foreach (var property in typeof(CurrentState).GetProperties()
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsNumeric(p.PropertyType))
                         .Select(p => p.Name)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                yield return property;
        }

        static bool IsNumeric(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.Boolean:
                    return true;
                default:
                    return false;
            }
        }

        static string WatchText(string source)
        {
            var parsed = AlertSource.Parse(source);
            if (parsed.Kind == AlertSourceKind.NamedFreshCount) return "Fresh Judicar values";
            return parsed.Kind == AlertSourceKind.Unknown ? source ?? "" : WatchLabel(new AlertOptions { Source = source });
        }

        public static string ParseWatch(string text, string fallback)
        {
            var value = (text ?? "").Trim();
            if (value.Length == 0) return fallback;
            if (value == "Fresh Judicar values") return AlertSource.NamedFreshCount;
            if (value == WatchLabel(new AlertOptions { Source = AlertSource.GpsFixCount })) return AlertSource.GpsFixCount;
            const string namedSuffix = " (named value)";
            if (value.EndsWith(namedSuffix, StringComparison.Ordinal))
                return AlertSource.Named(value.Substring(0, value.Length - namedSuffix.Length));
            if (AlertSource.Parse(value).Kind != AlertSourceKind.Unknown) return value;
            if (typeof(CurrentState).GetProperty(value) != null) return AlertSource.Field(value);
            return AlertSource.Named(value);
        }

        bool TryNumber(TextBox box, out double value, double minimum = double.MinValue)
        {
            var ok = double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                     !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum;
            MarkValid(box, ok || !box.Enabled);
            return ok;
        }

        void MarkValid(TextBox box, bool valid)
        {
            box.BackColor = valid ? _title.BackColor : Color.MistyRose;
            box.ForeColor = valid ? _title.ForeColor : Color.Black;
        }

        static object Value(ComboBox combo) => (combo.SelectedItem as Choice)?.Value;

        static void SelectChoice(ComboBox combo, object value)
        {
            foreach (Choice choice in combo.Items)
            {
                if (!Equals(choice.Value, value)) continue;
                combo.SelectedItem = choice;
                return;
            }
            combo.SelectedIndex = 0;
        }

        static ComboBox Combo(Choice[] choices, int width)
        {
            var combo = new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(choices.Cast<object>().ToArray());
            combo.SelectedIndex = 0;
            return combo;
        }

        static Choice ConditionChoice(CustomWarning.Conditional condition) =>
            new Choice { Text = AlertDescriptions.ConditionText(condition), Value = condition };

        static TextBox Box(int width) => new TextBox { Width = width };

        static Label Chip() => new Label
        {
            AutoSize = true,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(0, 0, 8, 0)
        };

        static Label FieldLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 3)
        };

        static Label InlineLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(4, 6, 4, 0)
        };

        static FlowLayoutPanel Inline(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.AddRange(controls);
            return panel;
        }
    }
}
