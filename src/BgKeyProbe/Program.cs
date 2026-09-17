using System;
using System.Linq;
using System.Text;
using System.Threading;
using OwHelper.Core;

class Program
{
    const int VK_SHIFT = 0x10;
    const int PulseIntervalMs = 2000;
    const string ProcessName = "Overwatch";

    static volatile bool loopStop = true;
    static GameWindow loopTarget;

    static string Label(GameWindow w) => $"{w.Class} 0x{w.Handle.ToInt64():X8}";

    static string Liveness(GameWindow w)
    {
        bool ok = w.IsResponding(out long ms);
        return ok
            ? $"正常响应（{ms} ms）"
            : $"无响应（{ms} ms 超时，消息队列未被处理）";
    }

    static void PrintOutcomes(PulseResult result, string prefix)
    {
        var parts = result.Messages.Select(m => m.Ok ? $"{m.Name}=True" : $"{m.Name}=False(err={m.Error})");
        string line = string.Join("  ", parts);
        string hint = result.Messages.Any(m => !m.Ok && m.Error == 5) ? "  <- 拒绝访问(5)，试管理员运行" : "";
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {prefix} {line}{hint}");
    }

    static void PulseShift(GameWindow w)
    {
        var result = PulseRunner.Execute(w.Handle, new PulseRecipe
        {
            Keys = new[] { VK_SHIFT },
            SendFocus = false,
            FocusWaitMs = 0,
            HoldMs = 200,
        });
        PrintOutcomes(result, $"PULSE {Label(w)}");
    }

    static void CountdownPulse(GameWindow w, int seconds)
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

    static void MatrixPulse(GameWindow w, bool activate, bool activateApp, bool focus, int holdMs)
    {
        var cursor = new CursorState();
        cursor.Save();
        PulseRunner.Execute(w.Handle, new PulseRecipe
        {
            Keys = new[] { VK_SHIFT },
            SendActivateApp = activateApp,
            SendActivate = activate,
            SendFocus = focus,
            FocusWaitMs = 50,
            HoldMs = holdMs,
        });
        cursor.RestoreSaved();
        CursorState.ReleaseClip();
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 矩阵脉冲 A={activate} APP={activateApp} F={focus} hold={holdMs}ms");
    }

    static void MousePulse(GameWindow w, int vk, string label, bool withMove)
    {
        var cursor = new CursorState();
        cursor.Save();
        if (!MouseInput.TryGetClientCenter(w.Handle, out int x, out int y))
        {
            Console.WriteLine("  无法获取客户区尺寸");
            return;
        }
        bool focus = PulseRunner.SendFocus(w.Handle);
        Thread.Sleep(50);
        string move = withMove ? MouseInput.SendMove(w.Handle, x, y).ToString() : "skip";
        bool down = MouseInput.SendButton(w.Handle, vk, down: true, x, y);
        Thread.Sleep(200);
        bool up = MouseInput.SendButton(w.Handle, vk, down: false, x, y);
        bool killFocus = PulseRunner.SendKillFocus(w.Handle);
        cursor.RestoreSaved();
        CursorState.ReleaseClip();
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {label} 焦点={focus}/{killFocus} 移动={move} 按下={down} 抬起={up} 坐标=({x},{y})");
    }

    static void StartLoop(GameWindow w)
    {
        StopLoop();
        loopTarget = w;
        loopStop = false;

        var thread = new Thread(() =>
        {
            while (!loopStop)
            {
                MatrixPulse(loopTarget, true, true, true, 200);
                for (int i = 0; i < PulseIntervalMs / 100 && !loopStop; i++) Thread.Sleep(100);
            }
        }) { IsBackground = true };
        thread.Start();
    }

    static void StopLoop()
    {
        if (loopStop) return;
        loopStop = true;
        Thread.Sleep(300);
        CursorState.ReleaseClip();
    }

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var windows = GameWindow.EnumerateAll(ProcessName);
        if (windows.Count == 0)
        {
            Console.WriteLine("未找到 Overwatch.exe，请先启动游戏。");
            return 1;
        }

        var process = windows[0].Process;
        string path = "未知";
        try { path = process.MainModule?.FileName ?? path; } catch { }
        Console.WriteLine($"Overwatch  PID={process.Id}  路径={path}");

        Console.WriteLine($"\n窗口 {windows.Count} 个 (缩进=子窗口):");
        foreach (var w in windows)
            Console.WriteLine($"  {new string(' ', w.Depth * 2)}{Label(w)}  visible={w.Visible}  minimized={w.Minimized}  {w.Width}x{w.Height}  \"{w.Title}\"");

        var target = windows.Where(x => x.Visible && !x.Minimized && x.Depth == 0).OrderByDescending(x => (long)x.Width * x.Height).FirstOrDefault()
                     ?? windows.OrderByDescending(x => (long)x.Width * x.Height).First();

        Console.WriteLine("\n命令:");
        Console.WriteLine("  回车 = 立刻发一次 Shift 脉冲（OW 保持在后台）");
        Console.WriteLine("  f    = 8 秒倒计时后发送（切回 OW 前台，测前台路径）");
        Console.WriteLine("  a    = 伪造激活(全三条)+Shift（基线方案）");
        Console.WriteLine("  b    = 只发 WM_SETFOCUS + Shift（测试能否不激活就收输入）");
        Console.WriteLine("  v    = 只发 WM_ACTIVATEAPP + Shift");
        Console.WriteLine("  u    = 全三条激活 + 30ms 超短窗口（测最短可触发窗口）");
        Console.WriteLine("  l    = 鼠标左键（MOUSEMOVE+DOWN/UP，窗口中心）");
        Console.WriteLine("  n    = 鼠标左键（不发 MOUSEMOVE，对比测试）");
        Console.WriteLine("  r    = 鼠标右键（同 l 序列）");
        Console.WriteLine("  m    = 鼠标中键（同 l 序列）");
        Console.WriteLine("  p    = 循环模式：每 2 秒 全三条激活+脉冲+取消激活（再按 p 停止）");
        Console.WriteLine("  q    = 退出");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"目标: {Label(target)}" + (loopStop ? "" : "   [循环运行中]"));
            Console.Write("> ");
            string line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();

            if (line.Equals("q", StringComparison.OrdinalIgnoreCase)) { StopLoop(); break; }
            if (line.Equals("f", StringComparison.OrdinalIgnoreCase)) { CountdownPulse(target, 8); continue; }
            if (line.Equals("a", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, true, true, true, 200); continue; }
            if (line.Equals("b", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, false, false, true, 200); continue; }
            if (line.Equals("v", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, false, true, false, 200); continue; }
            if (line.Equals("u", StringComparison.OrdinalIgnoreCase)) { MatrixPulse(target, true, true, true, 30); continue; }
            if (line.Equals("l", StringComparison.OrdinalIgnoreCase)) { MousePulse(target, MouseInput.VkLeftButton, "鼠标左键", withMove: true); continue; }
            if (line.Equals("n", StringComparison.OrdinalIgnoreCase)) { MousePulse(target, MouseInput.VkLeftButton, "鼠标左键(无移动)", withMove: false); continue; }
            if (line.Equals("r", StringComparison.OrdinalIgnoreCase)) { MousePulse(target, MouseInput.VkRightButton, "鼠标右键", withMove: true); continue; }
            if (line.Equals("m", StringComparison.OrdinalIgnoreCase)) { MousePulse(target, MouseInput.VkMiddleButton, "鼠标中键", withMove: true); continue; }
            if (line.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                if (loopStop) { StartLoop(target); Console.WriteLine("  循环脉冲已启动（输入 p 停止）"); }
                else { StopLoop(); Console.WriteLine("  循环已停止"); }
                continue;
            }
            if (line.Length != 0) continue;

            Console.WriteLine($"  队列探测: {Liveness(target)}");
            PulseShift(target);
        }
        return 0;
    }
}
