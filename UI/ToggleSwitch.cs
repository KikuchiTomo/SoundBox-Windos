using System.Drawing.Drawing2D;

namespace SoundBox.UI
{
    public class ToggleSwitch : Control
    {
        private bool _checked;
        private float _animPos;
        private System.Windows.Forms.Timer? _animTimer;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                StartAnimation();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CheckedChanged;

        public ToggleSwitch()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Size = new Size(36, 18);
            Cursor = Cursors.Hand;
            BackColor = DarkTheme.BgModule;
        }

        private void StartAnimation()
        {
            _animTimer?.Stop();
            _animTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _animTimer.Tick += (_, _) =>
            {
                float target = _checked ? 1f : 0f;
                _animPos += _checked ? 0.18f : -0.18f;
                _animPos = Math.Clamp(_animPos, 0, 1);
                Invalidate();
                if (Math.Abs(_animPos - target) < 0.01f)
                {
                    _animPos = target;
                    _animTimer.Stop();
                    Invalidate();
                }
            };
            _animTimer.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Checked = !Checked;
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Fill background
            using (var bgBrush = new SolidBrush(BackColor))
                g.FillRectangle(bgBrush, ClientRectangle);

            g.SmoothingMode = SmoothingMode.HighQuality;

            int h = Height;
            int w = Width;

            // Track
            var offColor = DarkTheme.BgElevated;
            var onColor = DarkTheme.Accent;
            var trackColor = Color.FromArgb(
                (int)(offColor.R + (onColor.R - offColor.R) * _animPos),
                (int)(offColor.G + (onColor.G - offColor.G) * _animPos),
                (int)(offColor.B + (onColor.B - offColor.B) * _animPos));

            using var trackBrush = new SolidBrush(trackColor);
            using var trackPath = new GraphicsPath();
            trackPath.AddArc(0, 0, h, h, 90, 180);
            trackPath.AddArc(w - h, 0, h, h, 270, 180);
            trackPath.CloseFigure();
            g.FillPath(trackBrush, trackPath);

            // Thumb
            int knobSize = h - 4;
            float knobX = 2 + _animPos * (w - h);
            using var knobBrush = new SolidBrush(DarkTheme.TextBright);
            g.FillEllipse(knobBrush, knobX, 2, knobSize, knobSize);
        }
    }
}
