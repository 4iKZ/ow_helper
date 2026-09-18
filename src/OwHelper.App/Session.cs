using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper;

public sealed class Session : IAsyncDisposable
{
    const string ProcessName = "Overwatch";

    readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    readonly Random rng = new Random();
    readonly ITargetLocator locator;
    readonly IPulseSender pulseSender;
    readonly Func<Process, IResourceGovernor> governorFactory;
    readonly IWindowPlacementController placement;
    readonly Action<string> output;
    readonly AppLog? log;
    readonly RuntimeStateStore? stateStore;
    PulseRecipe recipe;

    GameWindow? target;
    IResourceGovernor? governor;
    CancellationTokenSource? cts;
    CancellationTokenSource? wakeCts;
    Task? loopTask;
    readonly Queue<SessionNotice> notices = new Queue<SessionNotice>();
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
        Action<string> output,
        AppLog? log = null,
        RuntimeStateStore? stateStore = null)
    {
        this.recipe = recipe;
        this.locator = locator;
        this.pulseSender = pulseSender;
        this.governorFactory = governorFactory;
        this.placement = placement;
        this.output = output;
        this.log = log;
        this.stateStore = stateStore;
    }

    public SessionState State { get; private set; } = SessionState.Detached;
    public int IntervalSec { get; set; } = 30;
    public int JitterPercent { get; set; }
    public int ReattachPollMs { get; set; } = 2000;
    public string TargetProcessName { get; set; } = ProcessName;
    public ResourcePolicy Policy { get; set; } = ResourcePolicy.Default;
    public bool SkipWhenForeground { get; set; } = true;
    public bool AllowMoveOffscreen { get; set; } = true;
    public bool KeepOffscreenAcrossRestart { get; set; }
    public GameWindow? Target => target;
    public PulseRecipe Recipe => recipe;
    public int PulseCount => pulseCount;
    public int RunPulseCount { get; private set; }
    public DateTimeOffset? RunStartedAt { get; private set; }
    public DateTimeOffset? LastPulseAt { get; private set; }
    public bool IsOffscreen => placement.IsOffscreen;
    public bool LastResourceApplyPartial { get; private set; }
    public bool IsRunning => State is SessionState.Running or SessionState.Reattaching;

    public SessionNotice? DequeueNotice()
    {
        lock (notices)
        {
            return notices.Count > 0 ? notices.Dequeue() : null;
        }
    }

    public void UpdateRecipe(PulseRecipe updated) => recipe = updated;

    public void ApplyConfig(AppConfig config)
    {
        recipe = config.BuildRecipe();
        IntervalSec = config.Input.IntervalSeconds;
        JitterPercent = config.Input.JitterPercent;
        TargetProcessName = config.Target.ProcessName;
        Policy = config.BuildPolicy();
        SkipWhenForeground = config.Input.SkipWhenTargetForeground;
        AllowMoveOffscreen = config.Window.AllowMoveOffscreen;
        KeepOffscreenAcrossRestart = config.Window.KeepOffscreenAcrossRestart;
    }

    public async Task<ResourceApplyResult?> ApplyConfigAsync(AppConfig config)
    {
        await gate.WaitAsync();
        try
        {
            ApplyConfig(config);
            if (!IsRunning) return null;
            ResourceApplyResult? applied = ReapplyPolicyLocked();
            wakeCts?.Cancel();
            return applied;
        }
        finally { gate.Release(); }
    }

    ResourceApplyResult? ReapplyPolicyLocked()
    {
        if (governor == null) return null;
        ResourceApplyResult applied = governor.Apply(Policy);
        LastResourceApplyPartial = !applied.Success;
        LogResourceApply(applied);
        if (LastResourceApplyPartial)
        {
            Notify(new SessionNotice(SessionNoticeKind.ResourcePartialFailure));
        }
        return applied;
    }

    void Notify(SessionNotice notice)
    {
        lock (notices)
        {
            if (notices.Count < 32) notices.Enqueue(notice);
        }
    }

    public async Task<bool> AttachAsync(bool quiet = false)
    {
        await gate.WaitAsync();
        try { return AttachLocked(quiet); }
        finally { gate.Release(); }
    }

    bool AttachLocked(bool quiet = false)
    {
        target = locator.Find(TargetProcessName);
        if (target == null)
        {
            State = SessionState.WaitingForTarget;
            if (!quiet) Log(LogLevel.Information, "TARGET_LOST", message: TargetProcessName);
            return false;
        }
        governor = governorFactory(target.Process);
        State = SessionState.Ready;
        Log(LogLevel.Information, "TARGET_FOUND", pid: (int)target.Pid, hwnd: target.Handle, message: target.Class);
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
                    output("  还没有找到《守望先锋》，请先打开游戏");
                    Notify(new SessionNotice(SessionNoticeKind.GameNotFound));
                    return;
                }
            }
            State = SessionState.Starting;
            Interlocked.Exchange(ref finished, 0);
            failureStreak = 0;
            RunPulseCount = 0;
            RunStartedAt = DateTimeOffset.Now;
            ResourceApplyResult applied = governor!.Apply(Policy);
            LastResourceApplyPartial = !applied.Success;
            if (LastResourceApplyPartial)
            {
                Notify(new SessionNotice(SessionNoticeKind.ResourcePartialFailure));
            }
            output($"  资源策略：{Describe("已应用", "CPU 优先级", applied.Priority)}；{Describe("已应用", "EcoQoS", applied.Power)}");
            LogResourceApply(applied);
            cts = new CancellationTokenSource();
            loopTask = Task.Run(() => LoopAsync(cts.Token));
            State = SessionState.Running;
            Log(LogLevel.Information, "SESSION_START", pid: target == null ? null : (int)target.Pid, hwnd: target?.Handle);
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
                if (!AttachLocked())
                {
                    output("  还没有找到《守望先锋》，请先打开游戏");
                    Notify(new SessionNotice(SessionNoticeKind.GameNotFound));
                    return;
                }
                current = target!;
            }
            if (placement.NeedsRestore)
            {
                WindowPlacementResult result = placement.Restore(activate: true);
                output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
                LogPlacement("WINDOW_RESTORE", result, current);
                if (!placement.NeedsRestore) ClearPlacementState();
            }
            else if (!AllowMoveOffscreen)
            {
                output("  配置已禁用移出屏幕（window.allowMoveOffscreen=false）");
            }
            else
            {
                WindowPlacementResult result = placement.MoveOffscreen(current.Handle, (int)current.Pid, hideFromTaskbar: true);
                output(result.Success ? "  OW 窗口已移出屏幕（按 m 还原）" : $"  移出屏幕失败: {result.Message}{ErrorCode(result.NativeError)}");
                LogPlacement("WINDOW_MOVE_OFFSCREEN", result, current);
                if (result.Success) PersistPlacement(current);
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
            if (placement.NeedsRestore)
            {
                WindowPlacementResult result = placement.Restore();
                output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
                LogPlacement("WINDOW_RESTORE", result, target);
                if (!placement.NeedsRestore) ClearPlacementState();
            }
        }
        finally { gate.Release(); }
    }

    void PersistPlacement(GameWindow window)
    {
        if (stateStore == null) return;
        if (!placement.TryGetOriginalPosition(out int left, out int top)) return;
        stateStore.Save(new RuntimeState(
            Pid: (int)window.Pid,
            ProcessStartTimeUtc: window.Identity.ProcessStartTimeUtc,
            Hwnd: window.Handle.ToInt64(),
            Left: left,
            Top: top,
            TaskbarHidden: placement.StyleRestorePending,
            OriginalExStyle: placement.OriginalExStyle));
    }

    void ClearPlacementState() => stateStore?.Clear();

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
                    Notify(new SessionNotice(SessionNoticeKind.TargetLost));
                    Log(LogLevel.Warning, "TARGET_LOST", message: "reattach aborted");
                    break;
                }
                continue;
            }
            PulseRecipe recipeNow = recipe;
            bool skipNow = SkipWhenForeground;
            if (current.IsForeground && skipNow)
            {
                output($"  [{DateTime.Now:HH:mm:ss}] OW 在前台，本次跳过");
                Log(LogLevel.Information, "PULSE_SKIPPED_FOREGROUND", pid: (int)current.Pid, hwnd: current.Handle, pulseIndex: pulseCount + 1);
            }
            else
            {
                long started = Environment.TickCount64;
                PulseResult result = pulseSender.Execute(current.Handle, recipeNow);
                long elapsed = Environment.TickCount64 - started;
                pulseCount++;
                RunPulseCount++;
                LastPulseAt = DateTimeOffset.Now;
                if (result.AllSucceeded)
                {
                    failureStreak = 0;
                    Log(LogLevel.Information, "PULSE_OK", pid: (int)current.Pid, hwnd: current.Handle, pulseIndex: pulseCount, elapsedMs: elapsed);
                }
                else
                {
                    failureStreak++;
                    MessageOutcome first = result.Messages.First(m => !m.Ok);
                    Log(LogLevel.Warning, "PULSE_PARTIAL_FAILURE", pid: (int)current.Pid, hwnd: current.Handle, pulseIndex: pulseCount, nativeError: first.Error, operation: first.Name, elapsedMs: elapsed);
                    if (failureStreak >= 2 && !current.Validate().Valid)
                    {
                        Log(LogLevel.Warning, "TARGET_LOST", pid: (int)current.Pid, hwnd: current.Handle, message: "target invalid after consecutive pulse failures; reattaching now");
                        reattachRequested = true;
                    }
                    else if (failureStreak == 3)
                    {
                        Log(LogLevel.Error, "NATIVE_ERROR", pid: (int)current.Pid, nativeError: first.Error, operation: first.Name, message: "consecutive pulse failures");
                        output($"  警告：连续 3 次脉冲发送失败（{first.Name} err={first.Error}），可能需要以管理员身份运行");
                        Notify(new SessionNotice(SessionNoticeKind.PulseFailures));
                    }
                }
                output($"  [{DateTime.Now:HH:mm:ss}] 第 {pulseCount} 次脉冲完成");
            }
            if (!await WaitForNextPulseAsync(ct)) break;
        }
        await FinishAsync();
    }

    async Task<bool> WaitForNextPulseAsync(CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested) return false;
            if (reattachRequested) return true;
            int interval = IntervalSec;
            int jitter = JitterPercent;
            wakeCts?.Dispose();
            using var wake = new CancellationTokenSource();
            wakeCts = wake;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, wake.Token);
            try
            {
                await Task.Delay(NextWaitMs(interval, jitter), linked.Token);
                return true;
            }
            catch (TaskCanceledException)
            {
                // 被停止 / 重连 / 配置热应用打断：停止与重连交给外层，配置变更则按新设置重新计时
            }
        }
    }

    int NextWaitMs(int interval, int jitter)
    {
        double next = interval * (1.0 + (rng.NextDouble() * 2 - 1) * (jitter / 100.0));
        return Math.Max(1000, (int)(next * 1000));
    }

    public async Task<bool> ReattachAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!IsRunning) return AttachLocked();
            reattachRequested = true;
            try { wakeCts?.Cancel(); }
            catch (ObjectDisposedException) { }
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
        GameWindow? oldTarget;
        int oldPid;
        bool wasOffscreen = placement.IsOffscreen;
        await gate.WaitAsync();
        try
        {
            oldGovernor = governor;
            oldTarget = target;
            oldPid = oldTarget == null ? -1 : (int)oldTarget.Pid;
        }
        finally { gate.Release(); }

        Log(LogLevel.Warning, "TARGET_LOST", pid: oldPid < 0 ? null : oldPid);

        if (placement.NeedsRestore)
        {
            WindowPlacementResult restoredPlacement = placement.Restore();
            output(restoredPlacement.Success
                ? "  旧窗口位置已恢复"
                : $"  旧窗口恢复失败: {restoredPlacement.Message}{ErrorCode(restoredPlacement.NativeError)}");
            LogPlacement("WINDOW_RESTORE", restoredPlacement, target);
            if (!placement.NeedsRestore) ClearPlacementState();
        }
        if (oldGovernor != null)
        {
            ResourceRestoreResult restored = oldGovernor.Restore();
            output($"  旧目标资源已恢复：{Describe("已恢复", "CPU 优先级", restored.Priority)}；{Describe("已恢复", "EcoQoS", restored.Power)}");
            LogResourceRestore(restored);
        }

        await gate.WaitAsync();
        try
        {
            oldTarget?.Process.Dispose();
            target = null;
            governor = null;
            failureStreak = 0;
        }
        finally { gate.Release(); }

        while (!ct.IsCancellationRequested)
        {
            GameWindow? found = locator.Find(TargetProcessName);
            if (found != null && found.IsAlive)
            {
                await gate.WaitAsync();
                try
                {
                    target = found;
                    governor = governorFactory(found.Process);
                    ResourceApplyResult applied = governor.Apply(Policy);
                    LastResourceApplyPartial = !applied.Success;
                    if (LastResourceApplyPartial)
                    {
                        Notify(new SessionNotice(SessionNoticeKind.ResourcePartialFailure));
                    }
                    output($"  已重新连接: PID {oldPid} → {found.Pid}；资源策略：{Describe("已应用", "CPU 优先级", applied.Priority)}；{Describe("已应用", "EcoQoS", applied.Power)}");
                    Notify(new SessionNotice(SessionNoticeKind.TargetReattached, oldPid, (int)found.Pid));
                    LogResourceApply(applied);
                    Log(LogLevel.Information, "TARGET_REATTACHED", pid: (int)found.Pid, hwnd: found.Handle, message: $"{oldPid} -> {found.Pid}");
                    State = SessionState.Running;
                    if (wasOffscreen && KeepOffscreenAcrossRestart && AllowMoveOffscreen)
                    {
                        WindowPlacementResult moved = placement.MoveOffscreen(found.Handle, (int)found.Pid, hideFromTaskbar: true);
                        output(moved.Success ? "  已按配置将新窗口移出屏幕" : $"  新窗口移出屏幕失败: {moved.Message}{ErrorCode(moved.NativeError)}");
                        LogPlacement("WINDOW_MOVE_OFFSCREEN", moved, found);
                        if (moved.Success) PersistPlacement(found);
                    }
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
            LogResourceRestore(restored);
        }
        if (placement.NeedsRestore)
        {
            WindowPlacementResult result = placement.Restore();
            output(result.Success ? "  OW 窗口已还原" : $"  窗口还原失败: {result.Message}{ErrorCode(result.NativeError)}");
            LogPlacement("WINDOW_RESTORE", result, target);
            if (!placement.NeedsRestore) ClearPlacementState();
        }
        GameWindow? current = target;
        State = current != null && current.IsAlive ? SessionState.Ready : SessionState.WaitingForTarget;
        Log(LogLevel.Information, "SESSION_STOP", pid: current == null ? null : (int)current.Pid, pulseIndex: pulseCount);
        output("  ■ 挂机已停止");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await CleanupAsync();
        await gate.WaitAsync();
        try
        {
            target?.Process.Dispose();
            target = null;
            State = SessionState.Disposed;
        }
        finally { gate.Release(); }
        gate.Dispose();
    }

    void LogResourceApply(ResourceApplyResult applied)
    {
        bool partial = !applied.Success;
        Log(partial ? LogLevel.Warning : LogLevel.Information,
            partial ? "RESOURCE_APPLY_PARTIAL" : "RESOURCE_APPLY",
            nativeError: applied.Priority.NativeError ?? applied.Power.NativeError,
            operation: "policy",
            message: $"{applied.Priority.Name}={(applied.Priority.Success ? "ok" : applied.Priority.Message)}; {applied.Power.Name}={(applied.Power.Success ? "ok" : applied.Power.Message)}");
    }

    void LogResourceRestore(ResourceRestoreResult restored)
    {
        bool partial = !restored.Success;
        Log(partial ? LogLevel.Warning : LogLevel.Information,
            partial ? "RESOURCE_RESTORE_PARTIAL" : "RESOURCE_RESTORE",
            nativeError: restored.Priority.NativeError ?? restored.Power.NativeError,
            operation: "policy",
            message: $"{restored.Priority.Name}={(restored.Priority.Success ? "ok" : restored.Priority.Message)}; {restored.Power.Name}={(restored.Power.Success ? "ok" : restored.Power.Message)}");
    }

    void LogPlacement(string evt, WindowPlacementResult result, GameWindow? window)
    {
        Log(result.Success ? LogLevel.Information : LogLevel.Warning, evt,
            pid: window == null ? null : (int)window.Pid,
            hwnd: window?.Handle,
            nativeError: result.NativeError,
            operation: "SetWindowPos",
            message: result.Message);
    }

    void Log(
        LogLevel level,
        string evt,
        int? pid = null,
        nint? hwnd = null,
        int? pulseIndex = null,
        int? nativeError = null,
        string? operation = null,
        long? elapsedMs = null,
        string? message = null)
        => log?.Write(new LogEntry(DateTimeOffset.Now, level, evt, pid, hwnd, pulseIndex, nativeError, operation, elapsedMs, message));

    static string Describe(string verb, string label, OperationResult result)
    {
        if (result.Success) return $"{label} {verb}";
        string code = result.NativeError is int nativeError ? $" (Win32={nativeError})" : "";
        return $"{label} 失败: {result.Message}{code}";
    }

    static string ErrorCode(int? nativeError)
        => nativeError is int code ? $" (Win32={code})" : "";
}




