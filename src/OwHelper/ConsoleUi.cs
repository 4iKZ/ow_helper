using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper;

internal sealed class ConsoleUi
{
    readonly Session session;
    readonly string keysDisplay;
    readonly AppLog log;

    public ConsoleUi(Session session, string keysDisplay, AppLog log)
    {
        this.session = session;
        this.keysDisplay = keysDisplay;
        this.log = log;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("=== OW 后台挂机助手 ===");
        if (await session.AttachAsync()) PrintTarget();
        else Console.WriteLine("未找到 Overwatch.exe，启动游戏后按 r 检测。");
        PrintHelp();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q)
            {
                await session.CleanupAsync();
                return;
            }
            if (key.Key == ConsoleKey.Spacebar)
            {
                if (session.IsRunning) await session.StopAsync();
                else await session.StartAsync();
                continue;
            }
            if (key.Key == ConsoleKey.R)
            {
                await session.ReattachAsync();
                if (session.Target != null) PrintTarget();
                else Console.WriteLine("  仍未找到 OW");
                continue;
            }
            if (key.Key == ConsoleKey.M)
            {
                await session.ToggleOffscreenAsync();
                continue;
            }
            if (key.Key == ConsoleKey.S)
            {
                PrintStatus();
                continue;
            }
            if (key.Key == ConsoleKey.L)
            {
                PrintRecentLogs();
                continue;
            }
            if (key.KeyChar == '+' || key.KeyChar == '=')
            {
                session.IntervalSec = Math.Min(300, session.IntervalSec + 5);
                Console.WriteLine($"  间隔 = {session.IntervalSec}s");
                continue;
            }
            if (key.KeyChar == '-')
            {
                session.IntervalSec = Math.Max(5, session.IntervalSec - 5);
                Console.WriteLine($"  间隔 = {session.IntervalSec}s");
                continue;
            }
        }
    }

    void PrintTarget()
    {
        GameWindow? target = session.Target;
        if (target == null) return;
        Console.WriteLine($"已找到 OW: PID={target.Pid}  HWND=0x{target.Handle.ToInt64():X8}  {target.Width}x{target.Height}");
    }

    void PrintStatus()
    {
        Console.WriteLine("=== 状态 ===");
        Console.WriteLine($"State    : {session.State}");
        GameWindow? target = session.Target;
        if (target == null)
        {
            Console.WriteLine("Target   : (未检测)");
        }
        else
        {
            TargetValidationResult validation = target.Validate();
            Console.WriteLine($"Target   : PID {target.Pid}  HWND 0x{target.Handle.ToInt64():X8}  {target.Width}x{target.Height}");
            Console.WriteLine($"Alive    : {(validation.Valid ? "yes" : "no - " + validation.Reason)}");
        }
        Console.WriteLine($"Interval : {session.IntervalSec}s (jitter {session.JitterPercent}%)");
        string lastPulse = session.LastPulseAt is DateTimeOffset last ? $" (last {last:HH:mm:ss})" : "";
        Console.WriteLine($"Pulses   : {session.PulseCount}{lastPulse}");
        Console.WriteLine($"Log      : {log.FilePath}");
    }

    void PrintRecentLogs()
    {
        IReadOnlyList<string> lines = log.Tail(20);
        if (lines.Count == 0)
        {
            Console.WriteLine("  (暂无日志)");
            return;
        }
        foreach (string line in lines) Console.WriteLine("  " + line);
    }

    void PrintHelp()
    {
        Console.WriteLine($"\n按键: [{keysDisplay}]   间隔: {session.IntervalSec}s");
        Console.WriteLine("空格=开始/停止   m=移出屏幕/还原   r=重新检测   S=状态   L=最近日志   +/-=间隔增减 5s   q=退出\n");
    }
}
