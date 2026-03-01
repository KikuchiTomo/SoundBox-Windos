using System.Drawing.Drawing2D;

namespace SoundBox.UI
{
    public enum TransportType { Play, Stop, Record }

    public class TransportButton : Control
    {
        private bool _hovering;
        private bool _active;
        private TransportType _type;

        public TransportType Type { get => _type; set { _type = value; Invalidate(); } }
        public bool Active
        {
            get => _active;
            set { _active = value; Invalidate(); }
        }

        public TransportButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(40, 32);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // Background
            var bgColor = _hovering ? DarkTheme.BgElevated : DarkTheme.BgModule;
            using var bgBrush = new SolidBrush(bgColor);
            using var bgPath = CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 4);
            g.FillPath(bgBrush, bgPath);

            // Border
            var borderColor = _active ? GetActiveColor() : DarkTheme.Border;
            using var borderPen = new Pen(borderColor, 1f);
            g.DrawPath(borderPen, bgPath);

            // Icon
            int cx = Width / 2;
            int cy = Height / 2;
            Color iconColor = _active ? GetActiveColor() : DarkTheme.TextDim;

            switch (_type)
            {
                case TransportType.Play:
                    using (var brush = new SolidBrush(iconColor))
                    {
                        var triangle = new PointF[]
                        {
                            new(cx - 5, cy - 7),
                            new(cx + 7, cy),
                            new(cx - 5, cy + 7)
                        };
                        g.FillPolygon(brush, triangle);
                    }
                    break;

                case TransportType.Stop:
                    using (var brush = new SolidBrush(iconColor))
                        g.FillRectangle(brush, cx - 5, cy - 5, 10, 10);
                    break;

                case TransportType.Record:
                    using (var brush = new SolidBrush(iconColor))
                        g.FillEllipse(brush, cx - 6, cy - 6, 12, 12);
                    break;
            }
        }

        private Color GetActiveColor() => _type switch
        {
            TransportType.Play => DarkTheme.Accent,
            TransportType.Stop => DarkTheme.TextNormal,
            TransportType.Record => DarkTheme.Danger,
            _ => DarkTheme.Accent
        };

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
