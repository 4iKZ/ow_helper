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

    public override string ToString() => Ok ? $"{Name}=OK" : $"{Name}=FAIL(err={Error})";
}

public sealed class PulseResult
{
    public IReadOnlyList<MessageOutcome> Messages { get; init; } = Array.Empty<MessageOutcome>();
    public bool AllSucceeded => Messages.All(m => m.Ok);
}

public static class PulseRunner
{
    public static void Activate(IntPtr hwnd, bool activateApp, bool activate, bool focus)
    {
        if (activateApp) Native.PostMessage(hwnd, Native.WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero);
        if (activate) Native.PostMessage(hwnd, Native.WM_ACTIVATE, new IntPtr(Native.WA_ACTIVE), IntPtr.Zero);
        if (focus) Native.PostMessage(hwnd, Native.WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Deactivate(IntPtr hwnd, bool activateApp, bool activate, bool focus)
    {
        if (focus) Native.PostMessage(hwnd, Native.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        if (activate) Native.PostMessage(hwnd, Native.WM_ACTIVATE, new IntPtr(Native.WA_INACTIVE), IntPtr.Zero);
        if (activateApp) Native.PostMessage(hwnd, Native.WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool SendKey(IntPtr hwnd, int vk, bool down, bool repeat = false)
        => Native.PostMessage(hwnd, down ? Native.WM_KEYDOWN : Native.WM_KEYUP, new IntPtr(vk), KeyLParam(vk, !down, repeat));

    public static PulseResult Execute(IntPtr hwnd, PulseRecipe recipe)
    {
        var messages = new List<MessageOutcome>();
        if (recipe.SendActivateApp) messages.Add(Post(hwnd, "ACTIVATEAPP(1)", Native.WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero));
        if (recipe.SendActivate) messages.Add(Post(hwnd, "ACTIVATE(1)", Native.WM_ACTIVATE, new IntPtr(Native.WA_ACTIVE), IntPtr.Zero));
        if (recipe.SendFocus) messages.Add(Post(hwnd, "SETFOCUS", Native.WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero));
        if (recipe.FocusWaitMs > 0) Thread.Sleep(recipe.FocusWaitMs);
        foreach (int vk in recipe.Keys) messages.Add(Key(hwnd, vk, down: true));
        Thread.Sleep(recipe.HoldMs);
        for (int i = recipe.Keys.Count - 1; i >= 0; i--) messages.Add(Key(hwnd, recipe.Keys[i], down: false));
        if (recipe.SendFocus) messages.Add(Post(hwnd, "KILLFOCUS", Native.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero));
        if (recipe.SendActivate) messages.Add(Post(hwnd, "ACTIVATE(0)", Native.WM_ACTIVATE, new IntPtr(Native.WA_INACTIVE), IntPtr.Zero));
        if (recipe.SendActivateApp) messages.Add(Post(hwnd, "ACTIVATEAPP(0)", Native.WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero));
        return new PulseResult { Messages = messages };
    }

    static MessageOutcome Post(IntPtr hwnd, string name, uint msg, IntPtr wParam, IntPtr lParam)
    {
        bool ok = Native.PostMessage(hwnd, msg, wParam, lParam);
        return new MessageOutcome { Name = name, Ok = ok, Error = ok ? 0 : Marshal.GetLastWin32Error() };
    }

    static MessageOutcome Key(IntPtr hwnd, int vk, bool down)
    {
        bool ok = SendKey(hwnd, vk, down);
        return new MessageOutcome
        {
            Name = down ? $"KEYDOWN(0x{vk:X2})" : $"KEYUP(0x{vk:X2})",
            Ok = ok,
            Error = ok ? 0 : Marshal.GetLastWin32Error(),
        };
    }

    static IntPtr KeyLParam(int vk, bool keyUp, bool repeat)
    {
        uint scan = Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC);
        long v = 1L | ((long)scan << 16);
        if (keyUp) { v |= (1L << 30) | (1L << 31); }
        else if (repeat) { v |= (1L << 30); }
        return new IntPtr(v);
    }
}
