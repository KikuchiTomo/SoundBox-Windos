using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SoundBox.UI
{
    public class PitchDisplay : Control
    {
        private int _detectedNote = -1;
        private int _targetNote = -1;
        private float _pitchHz;
        private readonly string[] _noteNames = NativeDSP.NoteNames;

        public int DetectedNote { set { _detectedNote = value; Invalidate(); } }
        public int TargetNote { set { _targetNote = value; Invalidate(); } }
        public float PitchHz { set { _pitchHz = value; Invalidate(); } }

        public PitchDisplay()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Size = new Size(200, 52);
            BackColor = DarkTheme.BgDark;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background
            using (var bgBrush = new SolidBrush(DarkTheme.BgDark))
                g.FillRectangle(bgBrush, ClientRectangle);
            using (var borderPen = new Pen(DarkTheme.Border, 1f))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Piano note indicators
            int keyW = Width / 12;
            for (int i = 0; i < 12; i++)
            {
                int x = i * keyW;
                bool isBlack = i == 1 || i == 3 || i == 6 || i == 8 || i == 10;
                Color keyColor;

                if (i == _targetNote)
                    keyColor = DarkTheme.Accent;
                else if (i == _detectedNote)
                    keyColor = Color.FromArgb(100, DarkTheme.Warm);
                else
                    keyColor = isBlack ? DarkTheme.BgModule : DarkTheme.BgElevated;

                using var brush = new SolidBrush(keyColor);
                g.FillRectangle(brush, x + 1, 2, keyW - 2, 18);

                using var textBrush = new SolidBrush(i == _targetNote ? DarkTheme.TextBright : DarkTheme.TextMuted);
                var sz = g.MeasureString(_noteNames[i], DarkTheme.SmallFont);
                g.DrawString(_noteNames[i], DarkTheme.SmallFont, textBrush, x + (keyW - sz.Width) / 2, 4);
            }

            // Pitch info text
            string pitchText = _pitchHz > 0 ? $"{_pitchHz:F1} Hz" : "---";
            string noteText = _detectedNote >= 0 ? _noteNames[_detectedNote] : "--";
            string arrow = _targetNote >= 0 && _detectedNote >= 0 && _targetNote != _detectedNote
                ? " -> " + _noteNames[_targetNote] : "";

            using (var infoBrush = new SolidBrush(DarkTheme.TextNormal))
                g.DrawString($"{noteText}{arrow}  {pitchText}", DarkTheme.ValueFont, infoBrush, 4, 26);
        }
    }
}
