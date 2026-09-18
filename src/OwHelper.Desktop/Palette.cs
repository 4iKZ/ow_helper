using System.Drawing;

namespace OwHelper.Desktop;

public static class Palette
{
    public static readonly Color Paper = FromHex("#F3F4F6");
    public static readonly Color Panel = FromHex("#FFFFFF");
    public static readonly Color PanelHover = FromHex("#F9FAFB");
    public static readonly Color SurfaceSubtle = FromHex("#F8FAFC");
    public static readonly Color Border = FromHex("#E5E7EB");
    public static readonly Color BorderStrong = FromHex("#D1D5DB");
    public static readonly Color Ink = FromHex("#111827");
    public static readonly Color InkSecondary = FromHex("#4B5563");
    public static readonly Color InkMuted = FromHex("#9CA3AF");
    public static readonly Color Accent = FromHex("#EA580C");
    public static readonly Color AccentHover = FromHex("#C2410C");
    public static readonly Color AccentWash = FromHex("#FFEDD5");
    public static readonly Color AccentLight = FromHex("#FFF7ED");
    public static readonly Color StatusRunning = FromHex("#16A34A");
    public static readonly Color StatusWaiting = FromHex("#D97706");
    public static readonly Color StatusFaulted = FromHex("#DC2626");
    public static readonly Color StatusStopped = FromHex("#6B7280");

    static Color FromHex(string hex) => ColorTranslator.FromHtml(hex);
}

