using System.Drawing.Drawing2D;

namespace SoundBox.UI
{
    public class WaveformDisplay : Control
    {
        private readonly float[] _buffer = new float[512];
        private int _writePos;
        private readonly object _lock = new();

        public WaveformDisplay()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Height = 48;
            BackColor = DarkTheme.BgDark;
        }

        public void PushSamples(float[] samples, int count)
        {
            lock (_lock)
            {
                int step = Math.Max(1, count / 8);
                for (int i = 0; i < count; i += step)
                {
                    _buffer[_writePos] = samples[i];
                    _writePos = (_writePos + 1) % _buffer.Length;
                }
            }
            if (IsHandleCreated) BeginInvoke(Invalidate);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // Background
            using (var bgBrush = new SolidBrush(DarkTheme.BgDark))
                g.FillRectangle(bgBrush, ClientRectangle);
            using (var borderPen = new Pen(DarkTheme.Border, 1f))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Center line
            int centerY = Height / 2;
            using (var centerPen = new Pen(Color.FromArgb(40, DarkTheme.TextDim), 1f))
                g.DrawLine(centerPen, 0, centerY, Width, centerY);

            // Waveform
            lock (_lock)
            {
                if (_buffer.Length < 2 || Width < 2) return;
                using var wavePen = new Pen(DarkTheme.Accent, 1.5f);
                var points = new PointF[Width];
                for (int x = 0; x < Width; x++)
                {
                    int bufIdx = (_writePos + x * _buffer.Length / Width) % _buffer.Length;
                    float sample = _buffer[bufIdx];
                    float y = centerY - sample * (Height / 2 - 4);
                    points[x] = new PointF(x, Math.Clamp(y, 2, Height - 2));
                }
                if (points.Length > 1) g.DrawLines(wavePen, points);
            }
        }
    }
}
