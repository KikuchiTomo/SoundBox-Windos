using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SoundBox.UI
{
    public class Knob : Control
    {
        private float _value;
        private float _min;
        private float _max = 1.0f;
        private bool _dragging;
        private int _dragStartY;
        private float _dragStartValue;
        private string _label = "";
        private string _unit = "";
        private Color _knobColor;

        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Clamp(value, _min, _max);
                if (Math.Abs(_value - clamped) > 0.0001f)
                {
                    _value = clamped;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                }
            }
        }

        public float Minimum { get => _min; set { _min = value; Invalidate(); } }
        public float Maximum { get => _max; set { _max = value; Invalidate(); } }
        public string Label { get => _label; set { _label = value; Invalidate(); } }
        public string Unit { get => _unit; set { _unit = value; Invalidate(); } }
        public Color KnobColor { get => _knobColor; set { _knobColor = value; Invalidate(); } }

        public event EventHandler? ValueChanged;

        public Knob()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Size = new Size(62, 82);
            Cursor = Cursors.Hand;
            BackColor = DarkTheme.BgModule;
            _knobColor = DarkTheme.Accent;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartValue = _value;
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                float delta = (_dragStartY - e.Y) * (_max - _min) / 150f;
                Value = _dragStartValue + delta;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float step = (_max - _min) / 100f;
            Value += e.Delta > 0 ? step : -step;
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Fill background explicitly
            using (var bgBrush = new SolidBrush(BackColor))
                g.FillRectangle(bgBrush, ClientRectangle);

            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int knobDiam = Math.Min(Width - 6, Height - 32);
            if (knobDiam < 10) knobDiam = 10;
            int cx = Width / 2;
            int cy = 14 + knobDiam / 2;
            int radius = knobDiam / 2;

            // Label (top, centered)
            using (var labelBrush = new SolidBrush(DarkTheme.TextDim))
            {
                var sz = g.MeasureString(_label, DarkTheme.SmallFont);
                g.DrawString(_label, DarkTheme.SmallFont, labelBrush, cx - sz.Width / 2, 0);
            }

            // Background arc
            int arcRect = knobDiam - 2;
            var arcBounds = new Rectangle(cx - arcRect / 2, cy - arcRect / 2, arcRect, arcRect);

            using (var bgPen = new Pen(DarkTheme.BgElevated, 3.5f))
                g.DrawArc(bgPen, arcBounds, 135, 270);

            // Value arc
            float norm = (_max > _min) ? (_value - _min) / (_max - _min) : 0;
            float sweep = norm * 270f;
            if (sweep > 0.5f)
            {
                using var valuePen = new Pen(_knobColor, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(valuePen, arcBounds, 135, sweep);
            }

            // Knob body
            int bodyR = radius - 7;
            if (bodyR < 4) bodyR = 4;
            using (var bodyBrush = new SolidBrush(DarkTheme.BgModule))
                g.FillEllipse(bodyBrush, cx - bodyR, cy - bodyR, bodyR * 2, bodyR * 2);

            // Indicator line
            float angle = (135 + sweep) * (float)Math.PI / 180f;
            float ls = bodyR * 0.3f;
            float le = bodyR * 0.85f;
            using (var indPen = new Pen(_knobColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(indPen,
                    cx + ls * (float)Math.Cos(angle), cy + ls * (float)Math.Sin(angle),
                    cx + le * (float)Math.Cos(angle), cy + le * (float)Math.Sin(angle));
            }

            // Value text (below knob)
            string valueText = FormatValue();
            using (var valBrush = new SolidBrush(DarkTheme.TextNormal))
            {
                var sz = g.MeasureString(valueText, DarkTheme.ValueFont);
                g.DrawString(valueText, DarkTheme.ValueFont, valBrush, cx - sz.Width / 2, cy + radius + 2);
            }
        }

        private string FormatValue()
        {
            if (_unit == "dB")
            {
                float db = _value <= 0 ? -60f : 20f * (float)Math.Log10(_value);
                return db <= -60 ? "-inf" : $"{db:F1}";
            }
            if (_unit == "ms") return $"{_value:F0}ms";
            if (_unit == "%") return $"{_value:F0}%";
            if (_unit == "s") return $"{_value:F2}s";
            if (_unit == "x") return $"{_value:F1}x";
            return $"{_value:F2}";
        }
    }
}
