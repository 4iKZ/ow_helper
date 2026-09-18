using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace OwHelper.Core;

public readonly struct MessageOutcome
{
    public string Name { get; init; }
    public bool Ok { get; init; }
    public int Error { get; init; }
}

public sealed class PulseResult
{
    public IReadOnlyList<MessageOutcome> Messages { get; init; } = Array.Empty<MessageOutcome>();
    public bool AllSucceeded => Messages.All(m => m.Ok);
}

public static class PulseRunner
{
    public static bool SendFocus(IntPtr hwnd)
        => Native.PostMessage(hwnd, Native.WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

    public static bool SendKillFocus(IntPtr hwnd)
        => Native.PostMessage(hwnd, Native.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);

    public static PulseResult Execute(IntPtr hwnd, PulseRecipe recipe)
    {
        var messages = new List<MessageOutcome>();
        if (recipe.SendActivateApp) messages.Add(Post(hwnd, "ACTIVATEAPP(1)", Native.WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero));
        if (recipe.SendActivate) messages.Add(Post(hwnd, "ACTIVATE(1)", Native.WM_ACTIVATE, new IntPtr(Native.WA_ACTIVE), IntPtr.Zero));
        if (recipe.SendFocus) messages.Add(Outcome("SETFOCUS", SendFocus(hwnd)));
        if (recipe.FocusWaitMs > 0) Thread.Sleep(recipe.FocusWaitMs);

        int mouseX = 0;
        int mouseY = 0;
        bool hasMouse = recipe.Keys.Any(MouseInput.IsMouseVirtualKey);
        if (hasMouse)
        {
            MouseInput.TryGetClientCenter(hwnd, out mouseX, out mouseY);
            bool moved = MouseInput.SendMove(hwnd, mouseX, mouseY);
            messages.Add(new MessageOutcome
            {
                Name = "MOUSEMOVE",
                Ok = moved,
                Error = moved ? 0 : Marshal.GetLastWin32Error(),
            });
        }

        int state = 0;
        foreach (int vk in recipe.Keys)
        {
            state = MouseInput.ApplyKeyDown(state, vk);
            messages.Add(Key(hwnd, vk, down: true, mouseX, mouseY, state));
        }
        Thread.Sleep(recipe.HoldMs);
        for (int i = recipe.Keys.Count - 1; i >= 0; i--)
        {
            state = MouseInput.ApplyKeyUp(state, recipe.Keys[i]);
            messages.Add(Key(hwnd, recipe.Keys[i], down: false, mouseX, mouseY, state));
        }
        if (recipe.SendFocus) messages.Add(Outcome("KILLFOCUS", SendKillFocus(hwnd)));
        if (recipe.SendActivate) messages.Add(Post(hwnd, "ACTIVATE(0)", Native.WM_ACTIVATE, new IntPtr(Native.WA_INACTIVE), IntPtr.Zero));
        if (recipe.SendActivateApp) messages.Add(Post(hwnd, "ACTIVATEAPP(0)", Native.WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero));
        return new PulseResult { Messages = messages };
    }

    static MessageOutcome Post(IntPtr hwnd, string name, uint msg, IntPtr wParam, IntPtr lParam)
        => Outcome(name, Native.PostMessage(hwnd, msg, wParam, lParam));

    static MessageOutcome Outcome(string name, bool ok)
        => new MessageOutcome { Name = name, Ok = ok, Error = ok ? 0 : Marshal.GetLastWin32Error() };

    static MessageOutcome Key(IntPtr hwnd, int vk, bool down, int mouseX, int mouseY, int state)
    {
        if (MouseInput.IsMouseVirtualKey(vk))
        {
            bool sent = MouseInput.SendButton(hwnd, vk, down, mouseX, mouseY, state);
            return Outcome(down ? $"MOUSEDOWN(0x{vk:X2})" : $"MOUSEUP(0x{vk:X2})", sent);
        }

        uint scan = Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC);
        long bits = 1L | ((long)scan << 16);
        if (IsExtendedKey(vk)) bits |= 1L << 24;
        if (!down) bits |= (1L << 30) | (1L << 31);
        bool ok = Native.PostMessage(hwnd, down ? Native.WM_KEYDOWN : Native.WM_KEYUP, new IntPtr(vk), new IntPtr(bits));
        return Outcome(down ? $"KEYDOWN(0x{vk:X2})" : $"KEYUP(0x{vk:X2})", ok);
    }

    static bool IsExtendedKey(int vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 => true,
        0x2C or 0x2D or 0x2E => true,
        0x5B or 0x5C or 0x5D => true,
        0x6F or 0x90 => true,
        0xA3 or 0xA5 => true,
        _ => false,
    };
}
