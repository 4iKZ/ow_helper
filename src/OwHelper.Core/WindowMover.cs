using System;
using System.Runtime.InteropServices;

namespace OwHelper.Core;

public static class WindowMover
{
    public static WindowPlacementResult MoveTo(IntPtr hwnd, int pid, int left, int top)
    {
        if (!Native.IsWindow(hwnd))
        {
            return new WindowPlacementResult(false, "window handle is gone", null);
        }
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != (uint)pid)
        {
            return new WindowPlacementResult(false, $"hwnd belongs to pid {owner} (expected {pid})", null);
        }
        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER))
        {
            int error = Marshal.GetLastWin32Error();
            return new WindowPlacementResult(false, "SetWindowPos failed", error);
        }
        if (Native.GetWindowRect(hwnd, out Native.RECT after) && after.Left != left)
        {
            return new WindowPlacementResult(false, $"window did not move (left={after.Left})", null);
        }
        return new WindowPlacementResult(true, $"moved to ({left},{top})", null);
    }
}
