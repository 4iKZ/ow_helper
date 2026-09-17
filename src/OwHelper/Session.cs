using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OwHelper.Core;

sealed class Session
{
    const string ProcessName = "Overwatch";

    readonly Random rng = new Random();
    GameWindow target;
    ResourceGovernor governor;
    WindowPlacement placement;
    CancellationTokenSource cts;
    Task loopTask;
    int pulseCount;
    int failureStreak;
    int finished;

    public Session(IReadOnlyList<int> keys) => Recipe = new PulseRecipe { Keys = keys };

    public PulseRecipe Recipe { get; }
    public int IntervalSec { get; set; } = 30;
    public bool Running => loopTask != null && !loopTask.IsCompleted;

    public bool Attach()
    {
        target = GameWindow.Find(ProcessName);
        if (target == null) return false;
        governor = new ResourceGovernor(target.Process);
        placement = null;
        return true;
    }

    public void PrintTarget()
    {
        Console.WriteLine($"已找到 OW: PID={target.Pid}  HWND=0x{target.Handle.ToInt64():X8}  {target.Rect.Width}x{target.Rect.Height}");
    }

    public void Start()
    {
        if (Running) return;
        if (target == null || !target.IsAlive)
        {
            if (!Attach())
            {
                Console.WriteLine("  找不到 Overwatch.exe");
                return;
            }
        }
        Interlocked.Exchange(ref finished, 0);
        failureStreak = 0;
        governor.Apply();
        Console.WriteLine("  已开启：CPU 低优先级 + EcoQoS 节能");
        cts = new CancellationTokenSource();
        loopTask = Task.Run(() => LoopAsync(cts.Token));
        Console.WriteLine("  ▶ 挂机开始（空格停止）");
    }

    public void Stop()
    {
        if (!Running) return;
        cts.Cancel();
        try { loopTask.Wait(1500); } catch (AggregateException) { }
        Finish();
    }

    public void ToggleOffscreen()
    {
        if (target == null || !target.IsAlive)
        {
            Console.WriteLine("  尚未检测到 OW 窗口");
            return;
        }
        placement ??= new WindowPlacement(target.Handle);
        if (placement.IsOffscreen)
        {
            placement.Restore();
            Console.WriteLine("  OW 窗口已还原");
        }
        else
        {
            placement.MoveOffscreen();
            Console.WriteLine("  OW 窗口已移出屏幕（按 m 还原）");
        }
    }

    public void Cleanup()
    {
        Stop();
        if (placement != null && placement.IsOffscreen)
        {
            try
            {
                placement.Restore();
                Console.WriteLine("  OW 窗口已还原");
            }
            catch { }
        }
    }

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (target == null || !target.IsAlive)
            {
                if (!Attach())
                {
                    Console.WriteLine("  找不到 OW 窗口，挂机已停止");
                    break;
                }
            }
            if (target.IsForeground)
            {
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] OW 在前台，本次跳过");
            }
            else
            {
                PulseResult result = PulseRunner.Execute(target.Handle, Recipe);
                pulseCount++;
                if (result.AllSucceeded)
                {
                    failureStreak = 0;
                }
                else
                {
                    failureStreak++;
                    if (failureStreak == 3)
                    {
                        MessageOutcome first = result.Messages.First(m => !m.Ok);
                        Console.WriteLine($"  警告：连续 3 次脉冲发送失败（{first.Name} err={first.Error}），可能需要以管理员身份运行");
                    }
                }
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] 第 {pulseCount} 次脉冲完成");
            }
            try
            {
                await Task.Delay(Schedule.NextWaitMs(IntervalSec, rng), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        Finish();
    }

    void Finish()
    {
        if (Interlocked.Exchange(ref finished, 1) != 0) return;
        governor?.Restore();
        Console.WriteLine("  已恢复：CPU 正常优先级");
        Console.WriteLine("  ■ 挂机已停止");
    }
}
