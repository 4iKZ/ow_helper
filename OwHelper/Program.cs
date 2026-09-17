using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class OwHelper
{
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_SETFOCUS = 0x0007;
    const uint WM_KILLFOCUS = 0x0008;
    const uint MAPVK_VK_TO_VSC = 0;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOZORDER = 0x0004;
    const int PROCESS_POWER_THROTTLING = 4;

    const int FocusWaitMs = 50;
    const int HoldMs = 200;
    const int OffscreenX = -10000;
    const int OffscreenY = -10000;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, IntPtr info, uint infoSize);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    static volatile bool running = false;
    static volatile bool offscreen = false;
    static Thread loopThread;
    static IntPtr hwnd = IntPtr.Zero;
    static Process owProc;
    static RECT savedRect;
    static List<int> keys = new List<int> { 0x10 };
    static string keysDisplay = "shift";
    static int intervalSec = 30;
    static readonly Random rng = new Random();
    static int pulseCount = 0;

    static int VkOf(string name)
    {
        name = name.Trim().ToLowerInvariant();
        switch (name)
        {
            case "shift": return 0x10;
            case "ctrl": case "control": return 0x11;
            case "alt": return 0x12;
            case "space": return 0x20;
            case "enter": case "return": return 0x0D;
            case "tab": return 0x09;
            case "esc": case "escape": return 0x1B;
            case "up": return 0x26;
            case "down": return 0x28;
            case "left": return 0x25;
            case "right": return 0x27;
        }
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            return 0x70 + fn - 1;
        throw new ArgumentException($"未知按键: {name}");
    }

    static bool FindOw()
    {
        var procs = Process.GetProcessesByName("Overwatch");
        if (procs.Length == 0) return false;
        owProc = procs[0];
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint wpid);
            if (wpid != (uint)owProc.Id) return true;
            GetWindowRect(h, out RECT r);
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        if (best == IntPtr.Zero) return false;
        hwnd = best;
        return true;
    }

    static IntPtr KeyLParam(int vk, bool keyUp)
    {
        uint scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        long v = 1L | ((long)scan << 16);
        if (keyUp) v |= (1L << 30) | (1L << 31);
        return new IntPtr(v);
    }

    static bool Pulse()
    {
        if (!IsWindow(hwnd) && !FindOw()) return false;
        if (GetForegroundWindow() == hwnd)
        {
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] OW 在前台，本次跳过");
            return true;
        }
        PostMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(FocusWaitMs);
        foreach (var vk in keys)
            PostMessage(hwnd, WM_KEYDOWN, new IntPtr(vk), KeyLParam(vk, false));
        Thread.Sleep(HoldMs);
        for (int i = keys.Count - 1; i >= 0; i--)
            PostMessage(hwnd, WM_KEYUP, new IntPtr(keys[i]), KeyLParam(keys[i], true));
        PostMessage(hwnd, WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        pulseCount++;
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 第 {pulseCount} 次脉冲完成");
        return true;
    }

    static void LoopMain()
    {
        while (running)
        {
            if (!Pulse())
            {
                Console.WriteLine("  找不到 OW 窗口，挂机已停止");
                Stop();
                break;
            }
            double next = intervalSec * (0.85 + rng.NextDouble() * 0.3);
            int waitMs = Math.Max(1000, (int)(next * 1000));
            for (int i = 0; i < waitMs / 100 && running; i++) Thread.Sleep(100);
        }
    }

    static void SetEcoQoS(bool enable)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = enable ? 1u : 0u };
            int size = Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(state, ptr, false);
            SetProcessInformation(owProc.Handle, PROCESS_POWER_THROTTLING, ptr, (uint)size);
            Marshal.FreeHGlobal(ptr);
        }
        catch { }
    }

    static void ApplyThrottle()
    {
        try { owProc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        SetEcoQoS(true);
        Console.WriteLine("  已开启：CPU 低优先级 + EcoQoS 节能");
    }

    static void RestoreThrottle()
    {
        SetEcoQoS(false);
        try { owProc.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        Console.WriteLine("  已恢复：CPU 正常优先级");
    }

    static void SetOffscreen(bool on)
    {
        if (on)
        {
            GetWindowRect(hwnd, out savedRect);
            SetWindowPos(hwnd, IntPtr.Zero, OffscreenX, OffscreenY, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
            offscreen = true;
            Console.WriteLine("  OW 窗口已移出屏幕（按 m 还原）");
        }
        else
        {
            SetWindowPos(hwnd, IntPtr.Zero, savedRect.Left, savedRect.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
            offscreen = false;
            Console.WriteLine("  OW 窗口已还原");
        }
    }

    static void Start()
    {
        if (running) return;
        if (owProc == null || !IsWindow(hwnd))
        {
            if (!FindOw()) { Console.WriteLine("  找不到 Overwatch.exe"); return; }
        }
        running = true;
        ApplyThrottle();
        loopThread = new Thread(LoopMain) { IsBackground = true };
        loopThread.Start();
        Console.WriteLine("  ▶ 挂机开始（空格停止）");
    }

    static void Stop()
    {
        if (!running) return;
        running = false;
        if (loopThread != null && Thread.CurrentThread.ManagedThreadId != loopThread.ManagedThreadId)
            loopThread.Join(1500);
        RestoreThrottle();
        Console.WriteLine("  ■ 挂机已停止");
    }

    static void Cleanup()
    {
        Stop();
        if (offscreen)
        {
            try { SetOffscreen(false); } catch { }
        }
    }

    static void PrintFound()
    {
        GetWindowRect(hwnd, out RECT r);
        Console.WriteLine($"已找到 OW: PID={owProc.Id}  HWND=0x{hwnd.ToInt64():X8}  {r.Right - r.Left}x{r.Bottom - r.Top}");
    }

    static void PrintHelp()
    {
        Console.WriteLine($"\n按键: [{keysDisplay}]   间隔: {intervalSec}s (±15% 抖动)");
        Console.WriteLine("空格=开始/停止   m=窗口移出屏幕/还原   r=重新检测 OW   +/-=间隔增减 5s   q=退出\n");
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            try
            {
                keys = args[0].Split(',').Select(VkOf).ToList();
                keysDisplay = args[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("用法: OwHelper.exe [按键,逗号分隔] [间隔秒]   例: OwHelper.exe shift,w 30");
                return 1;
            }
        }
        if (args.Length > 1 && int.TryParse(args[1], out int iv) && iv >= 2) intervalSec = iv;

        Console.CancelKeyPress += (s, e) => { e.Cancel = true; Cleanup(); Environment.Exit(0); };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();

        Console.WriteLine("=== OW 后台挂机助手 ===");
        if (FindOw()) PrintFound();
        else Console.WriteLine("未找到 Overwatch.exe，启动游戏后按 r 检测。");
        PrintHelp();

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) { Cleanup(); return 0; }
            if (key.Key == ConsoleKey.Spacebar) { if (running) Stop(); else Start(); continue; }
            if (key.Key == ConsoleKey.R)
            {
                if (FindOw()) PrintFound(); else Console.WriteLine("  仍未找到 OW");
                continue;
            }
            if (key.Key == ConsoleKey.M)
            {
                if (IsWindow(hwnd)) SetOffscreen(!offscreen);
                else Console.WriteLine("  尚未检测到 OW 窗口");
                continue;
            }
            if (key.KeyChar == '+' || key.KeyChar == '=')
            {
                intervalSec = Math.Min(300, intervalSec + 5);
                Console.WriteLine($"  间隔 = {intervalSec}s");
                continue;
            }
            if (key.KeyChar == '-')
            {
                intervalSec = Math.Max(5, intervalSec - 5);
                Console.WriteLine($"  间隔 = {intervalSec}s");
                continue;
            }
        }
    }
}
