using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using OwHelper.Core;

class Program
{
    const int VK_SHIFT = 0x10;
    const int PulseIntervalMs = 2000;
    const int HoldRepeatMs = 50;
    const string ProcessName = "Overwatch";

    static volatile bool loopStop = true;
    static string loopKind = "";
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

    static void StartLoop(GameWindow w, string kind)
    {
        StopLoop();
        loopTarget = w;
        loopKind = kind;
        loopStop = false;

        var thread = new Thread(() =>
        {
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
                var cursor = new CursorState();
                cursor.Save();
                PulseRunner.Activate(loopTarget.Handle, true, true, true);
                cursor.RestoreSaved();
                Thread.Sleep(60);
                while (!loopStop)
                {
                    PulseRunner.SendKey(loopTarget.Handle, VK_SHIFT, down: true);
                    Thread.Sleep(120);
                    PulseRunner.SendKey(loopTarget.Handle, VK_SHIFT, down: false);
                    CursorState.ReleaseClip();
                    Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 点按已发送");
                    for (int i = 0; i < PulseIntervalMs / 100 && !loopStop; i++) Thread.Sleep(100);
                }
                PulseRunner.Deactivate(loopTarget.Handle, true, true, true);
                CursorState.ReleaseClip();
            }
            else
            {
                PulseRunner.Activate(loopTarget.Handle, true, true, true);
                Thread.Sleep(80);
                PulseRunner.SendKey(loopTarget.Handle, VK_SHIFT, down: true);
                int ticks = 0;
                long nextClip = Environment.TickCount64;
                while (!loopStop)
                {
                    PulseRunner.SendKey(loopTarget.Handle, VK_SHIFT, down: true, repeat: true);
                    ticks++;
                    if (Environment.TickCount64 >= nextClip)
                    {
                        CursorState.ReleaseClip();
                        nextClip = Environment.TickCount64 + 500;
                    }
                    if (ticks % 100 == 0)
                        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 持续按住中 (重复消息 {ticks} 条)");
                    Thread.Sleep(HoldRepeatMs);
                }
                PulseRunner.SendKey(loopTarget.Handle, VK_SHIFT, down: false);
                PulseRunner.Deactivate(loopTarget.Handle, true, true, true);
                CursorState.ReleaseClip();
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
        CursorState.ReleaseClip();
    }

    static int Main(string[] args)
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
        for (int i = 0; i < windows.Count; i++)
            Console.WriteLine($"  [{i}] {new string(' ', windows[i].Depth * 2)}{Label(windows[i])}  visible={windows[i].Visible}  minimized={windows[i].Minimized}  {windows[i].Rect.Width}x{windows[i].Rect.Height}  \"{windows[i].Title}\"");

        bool all = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        var target = windows.Where(x => x.Visible && !x.Minimized && x.Depth == 0).OrderByDescending(x => (long)x.Rect.Width * x.Rect.Height).FirstOrDefault()
                     ?? windows.OrderByDescending(x => (long)x.Rect.Width * x.Rect.Height).First();

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
