using System.Drawing;

namespace OwHelper.Desktop;

public static class Palette
{
    public static readonly Color Paper = FromHex("#F4F1EA");
    public static readonly Color Panel = FromHex("#FBF9F4");
    public static readonly Color Border = FromHex("#D8D2C4");
    public static readonly Color Ink = FromHex("#2B2724");
    public static readonly Color InkSecondary = FromHex("#6E675E");
    public static readonly Color Accent = FromHex("#C2542B");
    public static readonly Color AccentHover = FromHex("#A8461F");
    public static readonly Color AccentWash = FromHex("#E8CFC2");
    public static readonly Color StatusRunning = FromHex("#4F7A3A");
    public static readonly Color StatusWaiting = FromHex("#B07A1E");
    public static readonly Color StatusFaulted = FromHex("#A63A2E");
    public static readonly Color StatusStopped = FromHex("#8C857A");

    static Color FromHex(string hex) => ColorTranslator.FromHtml(hex);
}

