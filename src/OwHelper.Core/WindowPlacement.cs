using System;

namespace OwHelper.Core;

public sealed class WindowPlacement
{
    const int OffscreenX = -10000;
    const int OffscreenY = -10000;

    readonly IntPtr hwnd;
    Native.RECT saved;

    public bool IsOffscreen { get; private set; }

    public WindowPlacement(IntPtr hwnd) => this.hwnd = hwnd;

    public void MoveOffscreen()
    {
        Native.GetWindowRect(hwnd, out saved);
        Native.SetWindowPos(hwnd, IntPtr.Zero, OffscreenX, OffscreenY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
        IsOffscreen = true;
    }

    public void Restore()
    {
        Native.SetWindowPos(hwnd, IntPtr.Zero, saved.Left, saved.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
        IsOffscreen = false;
    }
}
