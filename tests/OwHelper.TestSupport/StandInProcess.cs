using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using OwHelper.Core;

namespace OwHelper.TestSupport;

public sealed class StandInProcess : IDisposable
{
    public const string Name = "OwHelperStandIn";

    public Process Process { get; }

    StandInProcess(Process process) => Process = process;

    public static StandInProcess Start(params string[] args)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, Name + ".exe");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (args.Length > 0) info.Arguments = string.Join(" ", args);
        Process process = Process.Start(info) ?? throw new InvalidOperationException("无法启动替身进程");
        if (Array.IndexOf(args, "nowindow") < 0) WaitForWindow(process, 10000);
        return new StandInProcess(process);
    }

    public static void WaitForWindow(Process process, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (GameWindow.Find(Name) != null) return;
            Thread.Sleep(50);
        }
        throw new TimeoutException($"替身进程窗口未在 {timeoutMs} ms 内出现");
    }

    public void Kill()
    {
        if (Process.HasExited) return;
        Process.Kill();
        Process.WaitForExit(5000);
    }

    public void Dispose()
    {
        Kill();
        Process.Dispose();
    }
}

public static class WindowProbe
{
    public static (int Left, int Top, int Right, int Bottom) GetRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out RECT rect);
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static (int Width, int Height) GetClientSize(IntPtr hwnd)
    {
        GetClientRect(hwnd, out RECT rect);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    static extern int GetWindowLong32(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    public static bool TrySetForeground(IntPtr hwnd)
    {
        if (SetForegroundWindow(hwnd)) return true;
        return GetForegroundWindow() == hwnd;
    }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    public static long GetExStyle(IntPtr hwnd)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);

    public static void Minimize(IntPtr hwnd) => ShowWindow(hwnd, 6);

    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);
}

