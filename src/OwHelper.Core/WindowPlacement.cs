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
    long originalExStyle;

    public WindowPlacement(IntPtr hwnd, int pid)
    {
        this.hwnd = hwnd;
        this.pid = pid;
    }

    public IntPtr Handle => hwnd;

    public int Pid => pid;

    public PlacementState State { get; private set; } = PlacementState.None;

    public bool IsOffscreen => State == PlacementState.Offscreen;

    public bool TaskbarHidden { get; private set; }

    public long OriginalExStyle => originalExStyle;

    public bool TryGetSavedPosition(out int left, out int top)
    {
        left = saved.Left;
        top = saved.Top;
        return State is PlacementState.Captured or PlacementState.Offscreen;
    }

    public WindowPlacementResult MoveOffscreen(bool hideFromTaskbar = false)
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

        if (hideFromTaskbar)
        {
            if (WindowStyle.IsMinimized(hwnd))
            {
                WindowStyle.EnsureShown(hwnd);
            }
            WindowStyleResult style = WindowStyle.HideFromTaskbar(hwnd, pid);
            if (!style.Success)
            {
                return new WindowPlacementResult(false, style.Message, style.NativeError);
            }
            originalExStyle = style.OriginalExStyle;
            TaskbarHidden = true;
        }

        saved = rect;
        State = PlacementState.Captured;

        WindowPlacementResult moved = WindowMover.MoveTo(hwnd, pid, OffscreenX, OffscreenY);
        if (!moved.Success)
        {
            if (TaskbarHidden)
            {
                WindowStyle.RestoreStyle(hwnd, pid, originalExStyle);
                TaskbarHidden = false;
            }
            return moved;
        }
        State = PlacementState.Offscreen;
        return moved;
    }

    public WindowPlacementResult Restore()
    {
        if (State == PlacementState.None && !TaskbarHidden)
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

        if (WindowStyle.IsMinimized(hwnd))
        {
            WindowStyle.EnsureShown(hwnd);
        }

        WindowPlacementResult moved = WindowMover.MoveTo(hwnd, pid, saved.Left, saved.Top);
        if (!moved.Success)
        {
            State = PlacementState.RestoreFailed;
            return moved;
        }

        if (TaskbarHidden)
        {
            WindowStyleResult style = WindowStyle.RestoreStyle(hwnd, pid, originalExStyle);
            TaskbarHidden = false;
            if (!style.Success)
            {
                State = PlacementState.RestoreFailed;
                return new WindowPlacementResult(false, style.Message, style.NativeError);
            }
        }

        State = PlacementState.None;
        return new WindowPlacementResult(true, "restored", null);
    }
}
