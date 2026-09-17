using System;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper;

internal sealed class ConsoleUi
{
    readonly Session session;
    readonly string keysDisplay;

    public ConsoleUi(Session session, string keysDisplay)
    {
        this.session = session;
        this.keysDisplay = keysDisplay;
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

    void PrintHelp()
    {
        Console.WriteLine($"\n按键: [{keysDisplay}]   间隔: {session.IntervalSec}s (±15% 抖动)");
        Console.WriteLine("空格=开始/停止   m=窗口移出屏幕/还原   r=重新检测 OW   +/-=间隔增减 5s   q=退出\n");
    }
}
