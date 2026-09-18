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
    readonly Func<IntPtr, int, int, int, WindowPlacementResult> moveTo;
    readonly Func<IntPtr, int, long, WindowStyleResult> restoreStyle;
    Native.RECT saved;
    long originalExStyle;

    public WindowPlacement(IntPtr hwnd, int pid)
        : this(hwnd, pid, WindowMover.MoveTo, WindowStyle.RestoreStyle)
    {
    }

    internal WindowPlacement(
        IntPtr hwnd, int pid,
        Func<IntPtr, int, int, int, WindowPlacementResult> moveTo,
        Func<IntPtr, int, long, WindowStyleResult> restoreStyle)
    {
        this.hwnd = hwnd;
        this.pid = pid;
        this.moveTo = moveTo;
        this.restoreStyle = restoreStyle;
    }

    public IntPtr Handle => hwnd;

    public int Pid => pid;

    public PlacementState State { get; private set; } = PlacementState.None;

    public bool PositionRestorePending { get; private set; }

    public bool StyleRestorePending { get; private set; }

    public bool NeedsRestore => PositionRestorePending || StyleRestorePending;

    public bool IsOffscreen => PositionRestorePending;

    public long OriginalExStyle => originalExStyle;

    public bool TryGetSavedPosition(out int left, out int top)
    {
        left = saved.Left;
        top = saved.Top;
        return PositionRestorePending;
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
            StyleRestorePending = true;
        }

        saved = rect;

        WindowPlacementResult moved = moveTo(hwnd, pid, OffscreenX, OffscreenY);
        if (!moved.Success)
        {
            string message = moved.Message;
            int? error = moved.NativeError;
            if (StyleRestorePending)
            {
                WindowStyleResult rollback = restoreStyle(hwnd, pid, originalExStyle);
                if (rollback.Success) StyleRestorePending = false;
                else { message += $"; rollback failed: {rollback.Message}"; error ??= rollback.NativeError; }
            }
            return new WindowPlacementResult(false, message, error);
        }
        PositionRestorePending = true;
        State = PlacementState.Offscreen;
        return moved;
    }

    public WindowPlacementResult Restore()
    {
        if (!NeedsRestore)
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

        string message = "";
        int? error = null;
        if (PositionRestorePending)
        {
            WindowPlacementResult moved = moveTo(hwnd, pid, saved.Left, saved.Top);
            if (moved.Success) PositionRestorePending = false;
            else { message += moved.Message; error ??= moved.NativeError; }
        }

        if (StyleRestorePending)
        {
            WindowStyleResult style = restoreStyle(hwnd, pid, originalExStyle);
            if (style.Success) StyleRestorePending = false;
            else { if (message.Length > 0) message += "; "; message += style.Message; error ??= style.NativeError; }
        }

        if (NeedsRestore)
        {
            State = PlacementState.RestoreFailed;
            return new WindowPlacementResult(false, message, error);
        }
        State = PlacementState.None;
        return new WindowPlacementResult(true, "restored", null);
    }
}
