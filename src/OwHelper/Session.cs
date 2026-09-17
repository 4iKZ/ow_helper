using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper;

internal sealed class Session : IAsyncDisposable
{
    const string ProcessName = "Overwatch";

    readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    readonly Random rng = new Random();
    readonly ITargetLocator locator;
    readonly IPulseSender pulseSender;
    readonly Func<Process, IResourceGovernor> governorFactory;
    readonly IWindowPlacementController placement;
    readonly Action<string> output;
    readonly PulseRecipe recipe;

    GameWindow? target;
    IResourceGovernor? governor;
    CancellationTokenSource? cts;
    CancellationTokenSource? wakeCts;
    Task? loopTask;
    volatile bool reattachRequested;
    int pulseCount;
    int failureStreak;
    int finished;
    int disposed;

    public Session(
        PulseRecipe recipe,
        ITargetLocator locator,
        IPulseSender pulseSender,
        Func<Process, IResourceGovernor> governorFactory,
        IWindowPlacementController placement,
        Action<string> output)
    {
        this.recipe = recipe;
        this.locator = locator;
        this.pulseSender = pulseSender;
        this.governorFactory = governorFactory;
        this.placement = placement;
        this.output = output;
    }

    public SessionState State { get; private set; } = SessionState.Detached;
    public int IntervalSec { get; set; } = 30;
    public int ReattachPollMs { get; set; } = 2000;
    public GameWindow? Target => target;
    public bool IsRunning => State is SessionState.Running or SessionState.Reattaching;

    public async Task<bool> AttachAsync()
    {
        await gate.WaitAsync();
        try { return AttachLocked(); }
        finally { gate.Release(); }
    }

    bool AttachLocked()
    {
        target = locator.Find(ProcessName);
        if (target == null)
        {
            State = SessionState.WaitingForTarget;
            return false;
        }
        governor = governorFactory(target.Process);
        State = SessionState.Ready;
        return true;
    }

    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (IsRunning || State == SessionState.Starting) return;
            if (target == null || !target.IsAlive)
            {
                if (!AttachLocked())
                {
                    output("  找不到 Overwatch.exe");
                    return;
                }
            }
            State = SessionState.Starting;
            Interlocked.Exchange(ref finished, 0);
            failureStreak = 0;
            ResourceApplyResult applied = governor!.Apply();
            output($"  资源策略：{Describe("已应用", "CPU 优先级", applied.Priority)}；{Describe("已应用", "EcoQoS", applied.Power)}");
            cts = new CancellationTokenSource();
            loopTask = Task.Run(() => LoopAsync(cts.Token));
            State = SessionState.Running;
            output("  ▶ 挂机开始（空格停止）");
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync()
    {
        Task? toWait = null;
        await gate.WaitAsync();
        try
        {
            if (!IsRunning) return;
            State = SessionState.Stopping;
            cts?.Cancel();
            toWait = loopTask;
        }
        finally { gate.Release(); }

        if (toWait != null)
        {
            try { await toWait.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { }
        }

        await gate.WaitAsync();
        try { FinishLocked(); }
        finally { gate.Release(); }
    }

    public async Task ToggleOffscreenAsync()
    {
        await gate.WaitAsync();
        try
        {
            GameWindow? current = target;
            if (current == null || !current.IsAlive)
            {
                output("  尚未检测到 OW 窗口");
                return;
            }
            if (placement.IsOffscreen)
            {
                WindowPlacementResult result = placement.Restore();
                output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
            }
            else
            {
                WindowPlacementResult result = placement.MoveOffscreen(current.Handle, (int)current.Pid);
                output(result.Success ? "  OW 窗口已移出屏幕（按 m 还原）" : $"  移出屏幕失败: {result.Message}{ErrorCode(result.NativeError)}");
            }
        }
        finally { gate.Release(); }
    }

    public async Task CleanupAsync()
    {
        await StopAsync();
        await gate.WaitAsync();
        try
        {
            if (placement.IsOffscreen)
            {
                WindowPlacementResult result = placement.Restore();
                output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
            }
        }
        finally { gate.Release(); }
    }

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            GameWindow? current = target;
            if (reattachRequested || current == null || !current.IsAlive)
            {
                reattachRequested = false;
                if (!await TryReattachAsync(ct))
                {
                    output("  找不到 OW 窗口，挂机已停止");
                    break;
                }
                continue;
            }
            if (current.IsForeground)
            {
                output($"  [{DateTime.Now:HH:mm:ss}] OW 在前台，本次跳过");
            }
            else
            {
                PulseResult result = pulseSender.Execute(current.Handle, recipe);
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
                        output($"  警告：连续 3 次脉冲发送失败（{first.Name} err={first.Error}），可能需要以管理员身份运行");
                    }
                }
                output($"  [{DateTime.Now:HH:mm:ss}] 第 {pulseCount} 次脉冲完成");
            }
            try
            {
                wakeCts = new CancellationTokenSource();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, wakeCts.Token);
                double next = IntervalSec * (0.85 + rng.NextDouble() * 0.3);
                await Task.Delay(Math.Max(1000, (int)(next * 1000)), linked.Token);
            }
            catch (TaskCanceledException)
            {
                if (ct.IsCancellationRequested) break;
            }
        }
        await FinishAsync();
    }

    public async Task<bool> ReattachAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!IsRunning) return AttachLocked();
            reattachRequested = true;
            wakeCts?.Cancel();
            return true;
        }
        finally { gate.Release(); }
    }

    async Task<bool> TryReattachAsync(CancellationToken ct)
    {
        await gate.WaitAsync();
        try { State = SessionState.Reattaching; }
        finally { gate.Release(); }
        output("  目标已失效，等待重新连接...");

        IResourceGovernor? oldGovernor;
        int oldPid;
        await gate.WaitAsync();
        try
        {
            oldGovernor = governor;
            GameWindow? oldTarget = target;
            oldPid = oldTarget == null ? -1 : (int)oldTarget.Pid;
        }
        finally { gate.Release(); }

        if (placement.IsOffscreen)
        {
            WindowPlacementResult restoredPlacement = placement.Restore();
            output(restoredPlacement.Success
                ? "  旧窗口位置已恢复"
                : $"  旧窗口恢复失败: {restoredPlacement.Message}{ErrorCode(restoredPlacement.NativeError)}");
        }
        if (oldGovernor != null)
        {
            ResourceRestoreResult restored = oldGovernor.Restore();
            output($"  旧目标资源已恢复：{Describe("已恢复", "CPU 优先级", restored.Priority)}；{Describe("已恢复", "EcoQoS", restored.Power)}");
        }

        await gate.WaitAsync();
        try
        {
            target = null;
            governor = null;
            failureStreak = 0;
        }
        finally { gate.Release(); }

        while (!ct.IsCancellationRequested)
        {
            GameWindow? found = locator.Find(ProcessName);
            if (found != null && found.IsAlive)
            {
                await gate.WaitAsync();
                try
                {
                    target = found;
                    governor = governorFactory(found.Process);
                    ResourceApplyResult applied = governor.Apply();
                    output($"  已重新连接: PID {oldPid} → {found.Pid}；资源策略：{Describe("已应用", "CPU 优先级", applied.Priority)}；{Describe("已应用", "EcoQoS", applied.Power)}");
                    State = SessionState.Running;
                }
                finally { gate.Release(); }
                return true;
            }
            try { await Task.Delay(ReattachPollMs, ct); }
            catch (TaskCanceledException) { break; }
        }
        return false;
    }

    async Task FinishAsync()
    {
        await gate.WaitAsync();
        try { FinishLocked(); }
        finally { gate.Release(); }
    }

    void FinishLocked()
    {
        if (Interlocked.Exchange(ref finished, 1) != 0) return;
        if (governor != null)
        {
            ResourceRestoreResult restored = governor.Restore();
            output($"  资源恢复：{Describe("已恢复", "CPU 优先级", restored.Priority)}；{Describe("已恢复", "EcoQoS", restored.Power)}");
        }
        if (placement.IsOffscreen)
        {
            WindowPlacementResult result = placement.Restore();
            output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
        }
        GameWindow? current = target;
        State = current != null && current.IsAlive ? SessionState.Ready : SessionState.WaitingForTarget;
        output("  ■ 挂机已停止");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await CleanupAsync();
        await gate.WaitAsync();
        try { State = SessionState.Disposed; }
        finally { gate.Release(); }
        gate.Dispose();
    }

    static string Describe(string verb, string label, OperationResult result)
    {
        if (result.Success) return $"{label} {verb}";
        string code = result.NativeError is int nativeError ? $" (Win32={nativeError})" : "";
        return $"{label} 失败: {result.Message}{code}";
    }

    static string ErrorCode(int? nativeError)
        => nativeError is int code ? $" (Win32={code})" : "";
}
