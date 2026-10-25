using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MissionPlanner.BSA.UI
{
    public class SpeechIndicator : Control
    {
        public const string Caption = "Speech";
        public static readonly Color OnColour = Color.ForestGreen;
        public static readonly Color OffColour = Color.Firebrick;

        readonly Func<bool> _speechOn;
        readonly Timer _timer = new Timer { Interval = 1000 };
        readonly ToolTip _toolTip = new ToolTip();
        bool _on;

        public SpeechIndicator() : this(() => MainV2.speechEnable)
        {
        }

        public SpeechIndicator(Func<bool> speechOn)
        {
            _speechOn = speechOn ?? (() => false);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(70, 17);
            _on = Read();
            UpdateToolTip();
            _timer.Tick += (s, e) => Apply(Read());
            _timer.Start();
        }

        public bool SpeechOn => _on;

        public Color DotColour => _on ? OnColour : OffColour;

        public void Poll() => Apply(Read());

        bool Read()
        {
            try
            {
                return _speechOn();
            }
            catch
            {
                return false;
            }
        }

        void Apply(bool on)
        {
            if (on == _on) return;
            _on = on;
            UpdateToolTip();
            Invalidate();
        }

        void UpdateToolTip() =>
            _toolTip.SetToolTip(this, (_on ? "Speech is on" : "Speech is off") + " (Config > Planner > Enable Speech)");

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var size = Math.Max(6, Math.Min(Height - 4, 12));
            var dot = new Rectangle(1, (Height - size) / 2, size, size);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(DotColour)) e.Graphics.FillEllipse(brush, dot);
            var textBounds = new Rectangle(dot.Right + 4, 0, Math.Max(0, Width - dot.Right - 4), Height);
            TextRenderer.DrawText(e.Graphics, Caption, Font, textBounds, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
