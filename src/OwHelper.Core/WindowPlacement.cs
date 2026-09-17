using System;
using System.Runtime.InteropServices;

namespace OwHelper.Core;

public enum PlacementState
{
    None,
    Captured,
    Offscreen,
    RestoreFailed,
    Stale,
}

public sealed record WindowPlacementResult(bool Success, string Message, int? NativeError);

public sealed class WindowPlacement
{
    const int OffscreenX = -10000;
    const int OffscreenY = -10000;

    readonly IntPtr hwnd;
    readonly int pid;
    Native.RECT saved;

    public WindowPlacement(IntPtr hwnd, int pid)
    {
        this.hwnd = hwnd;
        this.pid = pid;
    }

    public IntPtr Handle => hwnd;

    public int Pid => pid;

    public PlacementState State { get; private set; } = PlacementState.None;

    public bool IsOffscreen => State == PlacementState.Offscreen;

    public WindowPlacementResult MoveOffscreen()
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
        if (!Native.GetWindowRect(hwnd, out Native.RECT rect))
        {
            int error = Marshal.GetLastWin32Error();
            return new WindowPlacementResult(false, "GetWindowRect failed", error);
        }
        saved = rect;
        State = PlacementState.Captured;

        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, OffscreenX, OffscreenY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER))
        {
            int error = Marshal.GetLastWin32Error();
            return new WindowPlacementResult(false, "SetWindowPos failed", error);
        }
        if (Native.GetWindowRect(hwnd, out Native.RECT after) && after.Left != OffscreenX)
        {
            return new WindowPlacementResult(false, $"window did not move (left={after.Left})", null);
        }
        State = PlacementState.Offscreen;
        return new WindowPlacementResult(true, "moved offscreen", null);
    }

    public WindowPlacementResult Restore()
    {
        if (State == PlacementState.None)
        {
            return new WindowPlacementResult(true, "nothing to restore", null);
        }
        if (!Native.IsWindow(hwnd))
        {
            State = PlacementState.Stale;
            return new WindowPlacementResult(false, "window gone; snapshot discarded", null);
        }
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != (uint)pid)
        {
            State = PlacementState.Stale;
            return new WindowPlacementResult(false, $"hwnd belongs to pid {owner}; refused to move; snapshot discarded", null);
        }
        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, saved.Left, saved.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER))
        {
            int error = Marshal.GetLastWin32Error();
            State = PlacementState.RestoreFailed;
            return new WindowPlacementResult(false, "SetWindowPos failed", error);
        }
        State = PlacementState.None;
        return new WindowPlacementResult(true, "restored", null);
    }
}
