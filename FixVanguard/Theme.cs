namespace FixVanguard;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(20, 16, 30);
    public static readonly Color BackgroundLift = Color.FromArgb(32, 24, 46);
    public static readonly Color Card = Color.FromArgb(34, 26, 48);
    public static readonly Color CardHover = Color.FromArgb(46, 36, 64);
    public static readonly Color Console = Color.FromArgb(16, 12, 24);
    public static readonly Color Line = Color.FromArgb(68, 56, 90);
    public static readonly Color Text = Color.FromArgb(236, 230, 244);
    public static readonly Color Muted = Color.FromArgb(156, 146, 174);
    public static readonly Color Particle = Color.FromArgb(198, 184, 224);
    public static readonly Color Accent = Color.FromArgb(150, 128, 196);
    public static readonly Color AccentBright = Color.FromArgb(186, 160, 232);
    public static readonly Color Success = Color.FromArgb(46, 204, 113);
    public static readonly Color Danger = Color.FromArgb(231, 76, 90);
    public static readonly Color Warning = Color.FromArgb(230, 190, 110);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 18f);
    public static readonly Font CaptionFont = new("Segoe UI", 9f);
    public static readonly Font BodyFont = new("Segoe UI", 9.5f);
    public static readonly Font BodyStrongFont = new("Segoe UI Semibold", 9.5f);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 10.5f);
    public static readonly Font ConsoleFont = new("Segoe UI", 9f);
    public static readonly Font ConsoleTimeFont = new("Consolas", 8.5f);
}
