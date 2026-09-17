using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OwHelper.Core;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var keys = new List<int> { 0x10 };
        string keysDisplay = "shift";
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            try
            {
                keys = args[0].Split(',').Select(KeyNames.Parse).ToList();
                keysDisplay = args[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("用法: OwHelper.exe [按键,逗号分隔] [间隔秒]   例: OwHelper.exe shift,w 30");
                return 1;
            }
        }
        int intervalSec = 30;
        if (args.Length > 1 && int.TryParse(args[1], out int iv) && iv >= 2) intervalSec = iv;

        var session = new Session(keys) { IntervalSec = intervalSec };

        Console.CancelKeyPress += (s, e) => { e.Cancel = true; session.Cleanup(); Environment.Exit(0); };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => session.Cleanup();

        Console.WriteLine("=== OW 后台挂机助手 ===");
        if (session.Attach()) session.PrintTarget();
        else Console.WriteLine("未找到 Overwatch.exe，启动游戏后按 r 检测。");
        Console.WriteLine($"\n按键: [{keysDisplay}]   间隔: {session.IntervalSec}s (±15% 抖动)");
        Console.WriteLine("空格=开始/停止   m=窗口移出屏幕/还原   r=重新检测 OW   +/-=间隔增减 5s   q=退出\n");

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) { session.Cleanup(); return 0; }
            if (key.Key == ConsoleKey.Spacebar)
            {
                if (session.Running) session.Stop(); else session.Start();
                continue;
            }
            if (key.Key == ConsoleKey.R)
            {
                if (session.Attach()) session.PrintTarget(); else Console.WriteLine("  仍未找到 OW");
                continue;
            }
            if (key.Key == ConsoleKey.M)
            {
                session.ToggleOffscreen();
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
}
