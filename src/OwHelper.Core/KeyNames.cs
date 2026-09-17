using System;

namespace OwHelper.Core;

public static class KeyNames
{
    public static int Parse(string name)
    {
        name = name.Trim().ToLowerInvariant();
        switch (name)
        {
            case "shift": return 0x10;
            case "ctrl": case "control": return 0x11;
            case "alt": return 0x12;
            case "space": return 0x20;
            case "enter": case "return": return 0x0D;
            case "tab": return 0x09;
            case "esc": case "escape": return 0x1B;
            case "up": return 0x26;
            case "down": return 0x28;
            case "left": return 0x25;
            case "right": return 0x27;
        }
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            return 0x70 + fn - 1;
        throw new ArgumentException($"未知按键: {name}");
    }
}
