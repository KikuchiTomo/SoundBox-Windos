using System.Drawing.Drawing2D;

namespace SoundBox.UI
{
    public class LevelMeter : Control
    {
        private float _level;
        private float _peak;
        private float _peakDecay;
        private readonly System.Windows.Forms.Timer _decayTimer;
        private const int SegmentCount = 24;
        private const int SegmentGap = 2;
        private bool _vertical = true;

        public float Level
        {
            get => _level;
            set
            {
                _level = Math.Clamp(value, 0, 1);
                if (_level > _peak) { _peak = _level; _peakDecay = 0; }
                Invalidate();
            }
        }

        public bool Vertical { get => _vertical; set { _vertical = value; Invalidate(); } }

        public LevelMeter()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Size = _vertical ? new Size(14, 120) : new Size(200, 10);
            BackColor = DarkTheme.BgDark;

            _decayTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _decayTimer.Tick += (_, _) =>
            {
                _peakDecay += 0.03f;
                if (_peakDecay > 1.0f) _peak = Math.Max(_peak - 0.02f, 0);
                _level = Math.Max(_level - 0.04f, 0);
                Invalidate();
            };
            _decayTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Fill background
            using (var bgBrush = new SolidBrush(DarkTheme.BgDark))
                g.FillRectangle(bgBrush, ClientRectangle);

            if (_vertical) PaintVertical(g);
            else PaintHorizontal(g);
        }

        private void PaintVertical(Graphics g)
        {
            int segH = (Height - (SegmentCount - 1) * SegmentGap) / SegmentCount;
            if (segH < 1) segH = 1;

            for (int i = 0; i < SegmentCount; i++)
            {
                float segLevel = 1.0f - (float)i / SegmentCount;
                int y = i * (segH + SegmentGap);

                Color color = segLevel > 0.85f ? DarkTheme.MeterRed
                            : segLevel > 0.6f  ? DarkTheme.MeterYellow
                            : DarkTheme.MeterGreen;

                bool lit = segLevel <= _level;
                bool isPeak = Math.Abs(segLevel - _peak) < (1.0f / SegmentCount) && _peak > 0.01f;

                Color drawColor = (isPeak || lit) ? color : Color.FromArgb(30, color);
                using var brush = new SolidBrush(drawColor);
                g.FillRectangle(brush, 1, y, Width - 2, segH);
            }
        }

        private void PaintHorizontal(Graphics g)
        {
            int segW = (Width - (SegmentCount - 1) * SegmentGap) / SegmentCount;
            if (segW < 1) segW = 1;

            for (int i = 0; i < SegmentCount; i++)
            {
                float segLevel = (float)(i + 1) / SegmentCount;
                int x = i * (segW + SegmentGap);

                Color color = segLevel > 0.85f ? DarkTheme.MeterRed
                            : segLevel > 0.6f  ? DarkTheme.MeterYellow
                            : DarkTheme.MeterGreen;

                bool lit = segLevel <= _level;
                bool isPeak = Math.Abs(segLevel - _peak) < (1.0f / SegmentCount) && _peak > 0.01f;

                Color drawColor = (isPeak || lit) ? color : Color.FromArgb(25, color);
                using var brush = new SolidBrush(drawColor);
                g.FillRectangle(brush, x, 1, segW, Height - 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _decayTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
