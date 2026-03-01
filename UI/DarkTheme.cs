namespace SoundBox.UI
{
    public static class DarkTheme
    {
        // DAW-inspired dark palette
        public static readonly Color BgDark     = Color.FromArgb(18, 18, 22);
        public static readonly Color BgPanel    = Color.FromArgb(28, 28, 34);
        public static readonly Color BgModule   = Color.FromArgb(36, 36, 44);
        public static readonly Color BgElevated = Color.FromArgb(48, 48, 58);
        public static readonly Color Border     = Color.FromArgb(60, 60, 72);
        public static readonly Color BorderLight= Color.FromArgb(80, 80, 95);

        public static readonly Color Accent     = Color.FromArgb(0, 212, 170);    // Teal
        public static readonly Color AccentDim  = Color.FromArgb(0, 150, 120);
        public static readonly Color AccentGlow = Color.FromArgb(0, 255, 200);
        public static readonly Color Warm       = Color.FromArgb(255, 140, 50);    // Orange
        public static readonly Color Danger     = Color.FromArgb(255, 70, 70);

        public static readonly Color TextBright = Color.FromArgb(240, 240, 245);
        public static readonly Color TextNormal = Color.FromArgb(190, 190, 200);
        public static readonly Color TextDim    = Color.FromArgb(120, 120, 135);
        public static readonly Color TextMuted  = Color.FromArgb(80, 80, 95);

        public static readonly Color MeterGreen  = Color.FromArgb(0, 230, 118);
        public static readonly Color MeterYellow = Color.FromArgb(255, 214, 0);
        public static readonly Color MeterRed    = Color.FromArgb(255, 60, 60);

        // Fonts
        public static readonly Font TitleFont   = new("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font ModuleTitle = new("Segoe UI Semibold", 9f, FontStyle.Regular);
        public static readonly Font LabelFont   = new("Segoe UI", 8f, FontStyle.Regular);
        public static readonly Font ValueFont   = new("Consolas", 9f, FontStyle.Bold);
        public static readonly Font SmallFont   = new("Segoe UI", 7.5f, FontStyle.Regular);
        public static readonly Font BigValue    = new("Consolas", 12f, FontStyle.Bold);
    }
}
