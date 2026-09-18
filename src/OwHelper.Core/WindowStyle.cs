using System;
using System.Runtime.InteropServices;

namespace OwHelper.Core;

public sealed record WindowStyleResult(bool Success, string Message, int? NativeError, long OriginalExStyle);

public static class WindowStyle
{
    public static bool IsMinimized(IntPtr hwnd) => Native.IsIconic(hwnd);

    public static bool EnsureShown(IntPtr hwnd, bool activate)
        => Native.ShowWindow(hwnd, activate ? Native.SW_RESTORE : Native.SW_SHOWNOACTIVATE);

    public static WindowStyleResult HideFromTaskbar(IntPtr hwnd, int pid)
    {
        if (!CheckIdentity(hwnd, pid, out string reason))
        {
            return new WindowStyleResult(false, reason, null, 0);
        }

        long original = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        long updated = (original | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        if (updated != original)
        {
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, updated);
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        long check = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        bool ok = (check & Native.WS_EX_TOOLWINDOW) != 0 && (check & Native.WS_EX_APPWINDOW) == 0;
        return ok
            ? new WindowStyleResult(true, "hidden from taskbar", null, original)
            : new WindowStyleResult(false, "window refused taskbar style change", null, original);
    }

    public static WindowStyleResult RestoreStyle(IntPtr hwnd, int pid, long originalExStyle)
    {
        if (!CheckIdentity(hwnd, pid, out string reason))
        {
            return new WindowStyleResult(false, reason, null, originalExStyle);
        }

        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, originalExStyle);
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

        long check = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        return check == originalExStyle
            ? new WindowStyleResult(true, "window style restored", null, originalExStyle)
            : new WindowStyleResult(false, "window style mismatch after restore", null, originalExStyle);
    }

    static bool CheckIdentity(IntPtr hwnd, int pid, out string reason)
    {
        if (!Native.IsWindow(hwnd))
        {
            reason = "window handle is gone";
            return false;
        }
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != (uint)pid)
        {
            reason = $"hwnd belongs to pid {owner} (expected {pid})";
            return false;
        }
        reason = "";
        return true;
    }
}
