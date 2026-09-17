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
            case "lshift": return 0xA0;
            case "rshift": return 0xA1;
            case "ctrl": case "control": return 0x11;
            case "lctrl": return 0xA2;
            case "rctrl": return 0xA3;
            case "alt": return 0x12;
            case "lalt": return 0xA4;
            case "ralt": return 0xA5;
            case "space": return 0x20;
            case "enter": case "return": return 0x0D;
            case "tab": return 0x09;
            case "esc": case "escape": return 0x1B;
            case "up": return 0x26;
            case "down": return 0x28;
            case "left": return 0x25;
            case "right": return 0x27;
            case "insert": return 0x2D;
            case "delete": case "del": return 0x2E;
            case "home": return 0x24;
            case "end": return 0x23;
            case "pageup": return 0x21;
            case "pagedown": return 0x22;
            case "printscreen": return 0x2C;
            case "numlock": return 0x90;
            case "numdivide": return 0x6F;
            case "mouseleft": case "lmb": return MouseInput.VkLeftButton;
            case "mouseright": case "rmb": return MouseInput.VkRightButton;
            case "mousemiddle": case "mmb": return MouseInput.VkMiddleButton;
            case "mouse4": return MouseInput.VkXButton1;
            case "mouse5": return MouseInput.VkXButton2;
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
