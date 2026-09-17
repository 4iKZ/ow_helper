using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OwHelper.Core;

internal static class Native
{
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_ACTIVATE = 0x0006;
    internal const uint WM_SETFOCUS = 0x0007;
    internal const uint WM_KILLFOCUS = 0x0008;
    internal const uint WM_ACTIVATEAPP = 0x001C;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const int WA_ACTIVE = 1;
    internal const int WA_INACTIVE = 0;
    internal const uint MAPVK_VK_TO_VSC = 0;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    internal enum ProcessInformationClass
    {
        ProcessPowerThrottling = 4,
    }

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ClipCursor(IntPtr rect);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessInformation(IntPtr hProcess, ProcessInformationClass infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint infoSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
}
