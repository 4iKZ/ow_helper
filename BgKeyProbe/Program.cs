using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class Program
{
    const uint WM_NULL = 0x0000;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_ACTIVATE = 0x0006;
    const uint WM_SETFOCUS = 0x0007;
    const uint WM_KILLFOCUS = 0x0008;
    const uint WM_ACTIVATEAPP = 0x001C;
    const int WA_ACTIVE = 1;
    const int WA_INACTIVE = 0;
    const int VK_SHIFT = 0x10;
    const uint MAPVK_VK_TO_VSC = 0;
    const uint SMTO_ABORTIFHUNG = 0x0002;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool ClipCursor(IntPtr rect);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    class WinInfo
    {
        public IntPtr Handle;
        public string Cls = "";
        public string Title = "";
        public bool Visible;
        public bool Minimized;
        public int Width;
        public int Height;
        public int Depth;
    }

    static volatile bool loopStop = true;
    static string loopKind = "";
    static WinInfo loopTarget;

    const int PulseIntervalMs = 2000;
    const int HoldRepeatMs = 50;

    static void AddWindow(List<WinInfo> list, IntPtr h, int depth)
    {
        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        var title = new StringBuilder(256);
        GetWindowText(h, title, title.Capacity);
        GetWindowRect(h, out RECT r);
        list.Add(new WinInfo
        {
            Handle = h,
            Cls = cls.ToString(),
            Title = title.ToString(),
            Visible = IsWindowVisible(h),
            Minimized = IsIconic(h),
            Width = r.Right - r.Left,
            Height = r.Bottom - r.Top,
            Depth = depth,
        });
    }

    static List<WinInfo> WindowsOf(uint pid)
    {
        var list = new List<WinInfo>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint wpid);
            if (wpid != pid) return true;
            AddWindow(list, h, 0);
            EnumChildWindows(h, (c, __) =>
            {
                GetWindowThreadProcessId(c, out uint cpid);
                if (cpid == pid) AddWindow(list, c, 1);
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string Label(WinInfo w) => $"{w.Cls} 0x{w.Handle.ToInt64():X8}";

    static string Liveness(WinInfo w)
    {
        long t0 = Environment.TickCount64;
        IntPtr ok = SendMessageTimeout(w.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out _);
        long ms = Environment.TickCount64 - t0;
        return ok == IntPtr.Zero
            ? $"无响应（{ms} ms 超时，消息队列未被处理）"
            : $"正常响应（{ms} ms）";
    }

    static IntPtr KeyLParam(uint scan, bool keyUp, bool repeat)
    {
        long v = 1L | ((long)scan << 16);
        if (keyUp) { v |= (1L << 30) | (1L << 31); }
        else if (repeat) { v |= (1L << 30); }
        return new IntPtr(v);
    }

    static void PulseShift(WinInfo w)
    {
        uint scan = MapVirtualKey(VK_SHIFT, MAPVK_VK_TO_VSC);
        bool down = PostMessage(w.Handle, WM_KEYDOWN, new IntPtr(VK_SHIFT), KeyLParam(scan, false, false));
        int errDown = down ? 0 : Marshal.GetLastWin32Error();
        Thread.Sleep(200);
        bool up = PostMessage(w.Handle, WM_KEYUP, new IntPtr(VK_SHIFT), KeyLParam(scan, true, false));
        int errUp = up ? 0 : Marshal.GetLastWin32Error();
        string hint = !down && errDown == 5 ? "  <- 拒绝访问(5)，试管理员运行" : "";
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] PULSE {Label(w)}  down={down}{(down ? "" : $" err={errDown}")}  up={up}{(up ? "" : $" err={errUp}")}{hint}");
    }

    static void CountdownPulse(WinInfo w, int seconds)
    {
        Console.WriteLine($"  {seconds} 秒后发送，现在切到游戏窗口盯住技能效果...");
        for (int i = seconds; i > 0; i--)
        {
            Console.Write($"  {i}... \r");
            Thread.Sleep(1000);
        }
        Console.WriteLine();
        PulseShift(w);
    }

    static void FakeActivate(WinInfo w)
    {
        PostMessage(w.Handle, WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero);
        PostMessage(w.Handle, WM_ACTIVATE, new IntPtr(WA_ACTIVE), IntPtr.Zero);
        PostMessage(w.Handle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
    }

    static void FakeDeactivate(WinInfo w)
    {
        PostMessage(w.Handle, WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        PostMessage(w.Handle, WM_ACTIVATE, new IntPtr(WA_INACTIVE), IntPtr.Zero);
        PostMessage(w.Handle, WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero);
    }

    static void MatrixPulse(WinInfo w, bool activate, bool activateApp, bool focus, int pressHoldMs)
    {
        GetCursorPos(out POINT saved);
        if (activateApp) PostMessage(w.Handle, WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero);
        if (activate) PostMessage(w.Handle, WM_ACTIVATE, new IntPtr(WA_ACTIVE), IntPtr.Zero);
        if (focus) PostMessage(w.Handle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(50);
        uint scan = MapVirtualKey(VK_SHIFT, MAPVK_VK_TO_VSC);
        PostMessage(w.Handle, WM_KEYDOWN, new IntPtr(VK_SHIFT), KeyLParam(scan, false, false));
        Thread.Sleep(pressHoldMs);
        PostMessage(w.Handle, WM_KEYUP, new IntPtr(VK_SHIFT), KeyLParam(scan, true, false));
        SetCursorPos(saved.X, saved.Y);
        if (focus) PostMessage(w.Handle, WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        if (activate) PostMessage(w.Handle, WM_ACTIVATE, new IntPtr(WA_INACTIVE), IntPtr.Zero);
        if (activateApp) PostMessage(w.Handle, WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero);
        ClipCursor(IntPtr.Zero);
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 矩阵脉冲 A={activate} APP={activateApp} F={focus} hold={pressHoldMs}ms");
    }

    static void StartLoop(WinInfo w, string kind)
    {
        StopLoop();
        loopTarget = w;
        loopKind = kind;
        loopStop = false;

        var thread = new Thread(() =>
        {
            uint scan = MapVirtualKey(VK_SHIFT, MAPVK_VK_TO_VSC);
            if (kind == "p")
            {
                while (!loopStop)
                {
                    MatrixPulse(loopTarget, true, true, true, 200);
                    for (int i = 0; i < PulseIntervalMs / 100 && !loopStop; i++) Thread.Sleep(100);
                }
            }
            else if (kind == "s")
            {
                GetCursorPos(out POINT saved);
                FakeActivate(loopTarget);
                SetCursorPos(saved.X, saved.Y);
                Thread.Sleep(60);
                while (!loopStop)
                {
                    PostMessage(loopTarget.Handle, WM_KEYDOWN, new IntPtr(VK_SHIFT), KeyLParam(scan, false, false));
                    Thread.Sleep(120);
                    PostMessage(loopTarget.Handle, WM_KEYUP, new IntPtr(VK_SHIFT), KeyLParam(scan, true, false));
                    ClipCursor(IntPtr.Zero);
                    Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 点按已发送");
                    for (int i = 0; i < PulseIntervalMs / 100 && !loopStop; i++) Thread.Sleep(100);
                }
                FakeDeactivate(loopTarget);
                ClipCursor(IntPtr.Zero);
            }
            else
            {
                FakeActivate(loopTarget);
                Thread.Sleep(80);
                PostMessage(loopTarget.Handle, WM_KEYDOWN, new IntPtr(VK_SHIFT), KeyLParam(scan, false, false));
                int ticks = 0;
                long nextClip = Environment.TickCount64;
                while (!loopStop)
                {
                    PostMessage(loopTarget.Handle, WM_KEYDOWN, new IntPtr(VK_SHIFT), KeyLParam(scan, false, true));
                    ticks++;
                    if (Environment.TickCount64 >= nextClip)
                    {
                        ClipCursor(IntPtr.Zero);
                        nextClip = Environment.TickCount64 + 500;
                    }
                    if (ticks % 100 == 0)
                        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 持续按住中 (重复消息 {ticks} 条)");
                    Thread.Sleep(HoldRepeatMs);
                }
                PostMessage(loopTarget.Handle, WM_KEYUP, new IntPtr(VK_SHIFT), KeyLParam(scan, true, false));
                FakeDeactivate(loopTarget);
                ClipCursor(IntPtr.Zero);
            }
        }) { IsBackground = true };
        thread.Start();
    }

    static void StopLoop()
    {
        if (loopStop) return;
        loopStop = true;
        loopKind = "";
        Thread.Sleep(300);
        ClipCursor(IntPtr.Zero);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var processes = Process.GetProcessesByName("Overwatch");
        if (processes.Length == 0)
        {
            Console.WriteLine("未找到 Overwatch.exe，请先启动游戏。");
            return 1;
        }

        var process = processes[0];
        string path = "未知";
        try { path = process.MainModule?.FileName ?? path; } catch { }
        Console.WriteLine($"Overwatch  PID={process.Id}  路径={path}");

        var windows = WindowsOf((uint)process.Id);
        Console.WriteLine($"\n窗口 {windows.Count} 个 (缩进=子窗口):");
        for (int i = 0; i < windows.Count; i++)
            Console.WriteLine($"  [{i}] {new string(' ', windows[i].Depth * 2)}{Label(windows[i])}  visible={windows[i].Visible}  minimized={windows[i].Minimized}  {windows[i].Width}x{windows[i].Height}  \"{windows[i].Title}\"");

        if (windows.Count == 0) { Console.WriteLine("没有窗口，无法继续。"); return 2; }

        bool all = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        var target = windows.Where(x => x.Visible && !x.Minimized && x.Depth == 0).OrderByDescending(x => (long)x.Width * x.Height).FirstOrDefault()
                     ?? windows.OrderByDescending(x => (long)x.Width * x.Height).First();

        Console.WriteLine("\n命令:");
        Console.WriteLine("  回车 = 立刻发一次 Shift 脉冲（OW 保持在后台）");
        Console.WriteLine("  f    = 8 秒倒计时后发送（切回 OW 前台，测前台路径）");
        Console.WriteLine("  a    = 伪造激活(全三条)+Shift（基线方案）");
        Console.WriteLine("  b    = 只发 WM_SETFOCUS + Shift（测试能否不激活就收输入）");
        Console.WriteLine("  v    = 只发 WM_ACTIVATEAPP + Shift");
        Console.WriteLine("  u    = 全三条激活 + 30ms 超短窗口（测最短可触发窗口）");
        Console.WriteLine("  p    = 循环模式：每 2 秒 全三条激活+脉冲+取消激活（再按 p 停止）");
        Console.WriteLine("  s    = 点按模式：只激活一次，之后每 2 秒点按一次 Shift（再按 s 停止）");
        Console.WriteLine("  h    = 按住模式：保持激活并持续发送按住的 Shift 消息（再按 h 停止）");
        Console.WriteLine("  数字 = 切换目标窗口, all = 发给所有窗口, q = 退出");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine(all ? "模式: 发送到所有窗口" : $"目标: [{windows.IndexOf(target)}] {Label(target)}" + (loopStop ? "" : $"   [循环运行中: {loopKind}]"));
            Console.Write("> ");
            string line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();

            if (line.Equals("q", StringComparison.OrdinalIgnoreCase)) { StopLoop(); break; }
            if (line.Equals("all", StringComparison.OrdinalIgnoreCase)) { all = true; continue; }
            if (line.Equals("f", StringComparison.OrdinalIgnoreCase)) { CountdownPulse(target, 8); continue; }
            if (line.Equals("a", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, true, true, true, 200); continue; }
            if (line.Equals("b", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, false, false, true, 200); continue; }
            if (line.Equals("v", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, false, true, false, 200); continue; }
            if (line.Equals("u", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, true, true, true, 30); continue; }
            if (line.Equals("s", StringComparison.OrdinalIgnoreCase))
            {
                if (loopKind == "s") { StopLoop(); Console.WriteLine("  点按模式已停止"); }
                else { StartLoop(target, "s"); Console.WriteLine("  点按模式已启动（输入 s 停止）"); }
                continue;
            }
            if (line.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                if (loopKind == "p") { StopLoop(); Console.WriteLine("  循环已停止"); }
                else { StartLoop(target, "p"); Console.WriteLine("  循环脉冲已启动（输入 p 停止）"); }
                continue;
            }
            if (line.Equals("h", StringComparison.OrdinalIgnoreCase))
            {
                if (loopKind == "h") { StopLoop(); Console.WriteLine("  按住模式已停止"); }
                else { StartLoop(target, "h"); Console.WriteLine("  按住模式已启动（输入 h 停止）"); }
                continue;
            }
            if (int.TryParse(line, out int index) && index >= 0 && index < windows.Count) { all = false; target = windows[index]; continue; }
            if (line.Length != 0) continue;

            Console.WriteLine($"  队列探测: {Liveness(target)}");
            if (all) foreach (var w in windows) PulseShift(w);
            else PulseShift(target);
        }
        return 0;
    }
}
