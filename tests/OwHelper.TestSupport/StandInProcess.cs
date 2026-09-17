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

    public static StandInProcess Start()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, Name + ".exe");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process process = Process.Start(info) ?? throw new InvalidOperationException("无法启动替身进程");
        WaitForWindow(process, 10000);
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
