using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SoundBox.UI
{
    public class EffectModule : Panel
    {
        private readonly string _title;
        private readonly Color _accentColor;
        private readonly ToggleSwitch _toggle;
        private readonly Panel _contentPanel;
        private const int HeaderH = 30;

        public bool EffectEnabled
        {
            get => _toggle.Checked;
            set => _toggle.Checked = value;
        }

        public new event EventHandler? EnabledChanged;

        public EffectModule(string title, Color accentColor)
        {
            _title = title;
            _accentColor = accentColor;
            DoubleBuffered = true;
            BackColor = DarkTheme.BgModule;

            _toggle = new ToggleSwitch { BackColor = DarkTheme.BgModule };
            _toggle.CheckedChanged += (_, _) => EnabledChanged?.Invoke(this, EventArgs.Empty);
            Controls.Add(_toggle);

            _contentPanel = new Panel { BackColor = DarkTheme.BgModule };
            Controls.Add(_contentPanel);
        }

        public Panel Content => _contentPanel;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_toggle == null || _contentPanel == null) return;
            _toggle.Location = new Point(Width - _toggle.Width - 10, (HeaderH - _toggle.Height) / 2);
            _contentPanel.SetBounds(0, HeaderH, Width, Math.Max(0, Height - HeaderH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background
            using (var bg = new SolidBrush(DarkTheme.BgModule))
                g.FillRectangle(bg, ClientRectangle);

            // Accent strip on left
            using (var ab = new SolidBrush(_accentColor))
                g.FillRectangle(ab, 0, 4, 3, HeaderH - 8);

            // Title text
            using (var tb = new SolidBrush(DarkTheme.TextNormal))
                g.DrawString(_title.ToUpperInvariant(), DarkTheme.ModuleTitle, tb, 12, 8);

            // Border
            using (var pen = new Pen(DarkTheme.Border, 1f))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
