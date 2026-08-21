using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SkiaSharp.Views.Desktop;

namespace MissionPlanner.Controls
{
    public partial class QuickView : SkiaSharp.Views.Desktop.SKControl
    {
        [System.ComponentModel.Browsable(true)]
        public string desc
        {
            get { return _desc; } set { if (_desc == value) return; _desc = value; Invalidate(); }
        }

        double _number = -9999;

        [System.ComponentModel.Browsable(true)]
        public double number
        {
            get { return _number; }
            set
            {
                lock (this)
                {
                    if (_number.Equals(value))
                        return;
                    _number = value;
                    Invalidate();
                }
            }
        }

        string _numberformat = "0.00";
        private string _desc = "";
        private Color _numbercolor;
        private bool _nodata;

        /// <summary>
        /// Set while nothing is bound to this view - e.g. a named_value_float field the vehicle has not
        /// sent yet. Painting the number would show 0.00, which an operator reads as a genuine zero
        /// rather than as "no data", so dashes are drawn instead.
        /// </summary>
        [System.ComponentModel.Browsable(true)]
        public bool nodata
        {
            get { return _nodata; }
            set { if (_nodata == value) return; _nodata = value; Invalidate(); }
        }

        const string nodatatext = "---";

        // muted enough to read as inactive against this app's dark quick view, still legible if a
        // light theme is in use
        static readonly Color nodatacolor = Color.FromArgb(125, 125, 125);

        [System.ComponentModel.Browsable(true)]
        public string numberformat
        {
            get
            {
                return _numberformat;
            }
            set
            {
                if (_numberformat.Equals(value))
                    return;
                _numberformat = value;
                this.Invalidate();
            }
        }

        [System.ComponentModel.Browsable(true)]
        public Color numberColor { get { return _numbercolor; } set { if (_numbercolor == value) return; _numbercolor = value; Invalidate(); } }

        //We use this property as a backup store for the numberColor, so it is possible to change numberColor temporary.
        public Color numberColorBackup { get; set; }

        //ThemeManager assigns numberColor by control name and runs after Activate(), so it needs telling
        //to leave a view alone once the operator has pinned its colours.
        public bool colourLocked { get; set; }

        //Same as numberColorBackup, for the description colour.
        public Color foreColorBackup { get; set; }

        public QuickView()
        {
            InitializeComponent();

            PaintSurface+= OnPaintSurface;
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e2)
        {
            var e = new SkiaGraphics(e2.Surface);
            e2.Surface.Canvas.Clear();
            int y = 0;
            {
                Size extent = e.MeasureString(desc, this.Font).ToSize();

                var mid = extent.Width / 2;

                e.DrawString(desc, this.Font, new SolidBrush(this.ForeColor), this.Width / 2 - mid, 5);

                y = extent.Height;
            }
            //
            {
                var numb = _nodata ? nodatatext : number.ToString(numberformat);

                //Sized to the widest string this view could show, so the number does not restyle
                //itself every time the value gains or loses a digit.
                var size = fitFontSize("0".PadLeft(numb.Length + 1, '0'), e, this.Height - y);

                using (var font = new Font(this.Font.FontFamily, size, this.Font.Style))
                {
                    Size extent = e.MeasureString(numb, font).ToSize();

                    e.DrawString(numb, font, new SolidBrush(_nodata ? nodatacolor : this.numberColor),
                        this.Width / 2 - extent.Width / 2, y + ((this.Height - y) / 2 - extent.Height / 2));
                }
            }
        }

        /// <summary>
        /// Largest font size that fits <paramref name="widest"/> into the space left under the
        /// description.
        ///
        /// Derived from a fixed probe size on every paint. It used to be derived from the size the
        /// previous paint happened to land on, which made the result depend on the control's resize
        /// history rather than on its geometry: two identically sized views could settle on different
        /// sizes, the size could oscillate as the value changed width, and one bad measurement was
        /// permanent because nothing ever reset the carried value.
        /// </summary>
        float fitFontSize(string widest, SkiaGraphics e, int available)
        {
            float size;

            using (var probe = new Font(this.Font.FontFamily, probeFontSize, this.Font.Style))
            {
                Size extent = e.MeasureString(widest, probe).ToSize();
                if (extent.Width <= 0 || extent.Height <= 0)
                    return minFontSize;

                float hRatio = available / (float)extent.Height;
                float wRatio = this.Width / (float)extent.Width;

                size = probeFontSize * (hRatio < wRatio ? hRatio : wRatio);
            }

            //quantised, so a pixel of layout drift does not resize the number
            size -= size % 5;

            if (!(size >= minFontSize && size <= maxFontSize))
                size = minFontSize;

            return size;
        }

        public override void Refresh()
        {
            if (this.Visible)
                base.Refresh();
        }

        protected override void WndProc(ref Message m) // seems to crash here on linux... so try ignore it
        {
            try
            {
                base.WndProc(ref m);
            }
            catch { }
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            if (this.Visible && this.ThisReallyVisible())
                base.OnInvalidated(e);
        }

        const float probeFontSize = 40f;
        const float minFontSize = 8f;
        const float maxFontSize = 999999f;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }
    }
}
