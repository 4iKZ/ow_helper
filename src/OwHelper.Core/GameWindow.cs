using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace OwHelper.Core;

public sealed class GameWindow
{
    public IntPtr Handle { get; private set; }
    public uint Pid { get; private set; }
    public string Class { get; private set; } = "";
    public string Title { get; private set; } = "";
    public bool Visible { get; private set; }
    public bool Minimized { get; private set; }
    public int Depth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Process Process { get; private set; } = null!;
    public TargetIdentity Identity { get; private set; } = null!;

    GameWindow() { }

    public bool IsAlive => Validate().Valid;
    public bool IsForeground => Native.GetForegroundWindow() == Handle;

    public TargetValidationResult Validate()
        => TargetValidation.Validate(Handle, (int)Pid, Process, Identity?.ProcessStartTimeUtc);

    public bool IsResponding(out long elapsedMs)
    {
        long t0 = Environment.TickCount64;
        IntPtr ok = Native.SendMessageTimeout(Handle, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 1500, out _);
        elapsedMs = Environment.TickCount64 - t0;
        return ok != IntPtr.Zero;
    }

    public static GameWindow? Find(string processName)
    {
        Process? process = FirstProcess(processName);
        return process == null ? null : Find(process);
    }

    public static GameWindow? Find(Process process)
        => Enumerate((uint)process.Id, false, process)
            .OrderByDescending(w => (long)w.Width * w.Height)
            .FirstOrDefault();

    public static List<GameWindow> EnumerateAll(string processName)
    {
        Process? process = FirstProcess(processName);
        return process == null ? new List<GameWindow>() : EnumerateAll(process);
    }

    public static List<GameWindow> EnumerateAll(Process process)
        => Enumerate((uint)process.Id, true, process);

    static Process? FirstProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        return processes.Length == 0 ? null : processes[0];
    }

    static List<GameWindow> Enumerate(uint pid, bool children, Process process)
    {
        var list = new List<GameWindow>();
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out uint wpid);
            if (wpid != pid) return true;
            list.Add(Snapshot(h, 0, pid, process));
            if (children)
            {
                Native.EnumChildWindows(h, (c, __) =>
                {
                    Native.GetWindowThreadProcessId(c, out uint cpid);
                    if (cpid == pid) list.Add(Snapshot(c, 1, pid, process));
                    return true;
                }, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static GameWindow Snapshot(IntPtr h, int depth, uint pid, Process process)
    {
        var cls = new StringBuilder(256);
        Native.GetClassName(h, cls, cls.Capacity);
        var title = new StringBuilder(256);
        Native.GetWindowText(h, title, title.Capacity);
        Native.GetWindowRect(h, out Native.RECT r);
        string className = cls.ToString();
        string windowTitle = title.ToString();
        return new GameWindow
        {
            Handle = h,
            Pid = pid,
            Class = className,
            Title = windowTitle,
            Visible = Native.IsWindowVisible(h),
            Minimized = Native.IsIconic(h),
            Depth = depth,
            Width = r.Right - r.Left,
            Height = r.Bottom - r.Top,
            Process = process,
            Identity = new TargetIdentity((int)pid, h, SafeProcessName(process), className, windowTitle, TryGetStartTimeUtc(process)),
        };
    }

    static string SafeProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return ""; }
    }

    static DateTime? TryGetStartTimeUtc(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }
}
