using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OwHelper;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.App.Tests;

public class SessionLifecycleTests
{
    sealed class FakeLocator : ITargetLocator
    {
        public GameWindow? Next;
        public GameWindow? Find(string processName) => Next;
    }

    sealed class FakePulseSender : IPulseSender
    {
        public int Calls;
        public bool Fail;
        public PulseRecipe? LastRecipe;
        public Action? OnExecute;

        public PulseResult Execute(IntPtr hwnd, PulseRecipe recipe)
        {
            Calls++;
            LastRecipe = recipe;
            OnExecute?.Invoke();
            bool ok = !Fail;
            return new PulseResult
            {
                Messages = new[] { new MessageOutcome { Name = "SETFOCUS", Ok = ok, Error = ok ? 0 : 5 } },
            };
        }
    }

    sealed class FakeGovernor : IResourceGovernor
    {
        public int ApplyCalls;
        public int RestoreCalls;
        public bool PriorityFails;
        public bool RestoreFails;

        public ResourceSnapshot? Snapshot => null;

        public ResourceApplyResult Apply(ResourcePolicy policy)
        {
            ApplyCalls++;
            OperationResult priority = PriorityFails
                ? new OperationResult("Priority", false, 5, "Access denied")
                : Ok("Priority");
            return new ResourceApplyResult(priority, Ok("Power"));
        }

        public ResourceRestoreResult Restore()
        {
            RestoreCalls++;
            return new ResourceRestoreResult(
                RestoreFails ? new OperationResult("Priority", false, 5, "Access denied") : Ok("Priority"),
                Ok("Power"));
        }

        static OperationResult Ok(string name) => new OperationResult(name, true, null, "ok");
    }

    sealed class FakePlacement : IWindowPlacementController
    {
        public int MoveCalls;
        public int RestoreCalls;

        public bool IsOffscreen { get; private set; }
        public bool StyleRestorePending => hidden;
        public bool NeedsRestore => IsOffscreen || hidden;
        public long OriginalExStyle => 0;

        bool hidden;

        public WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid, bool hideFromTaskbar)
        {
            MoveCalls++;
            IsOffscreen = true;
            hidden = hideFromTaskbar;
            return new WindowPlacementResult(true, "moved", null);
        }

        public WindowPlacementResult Restore(bool activate = false)
        {
            RestoreCalls++;
            LastRestoreActivate = activate;
            IsOffscreen = false;
            hidden = false;
            return new WindowPlacementResult(true, "restored", null);
        }

        public bool? LastRestoreActivate { get; private set; }

        public bool TryGetOriginalPosition(out int left, out int top)
        {
            left = 0;
            top = 0;
            return false;
        }
    }

    sealed class Harness : IAsyncDisposable
    {
        public readonly FakeWindow Window;
        public readonly FakeLocator Locator = new FakeLocator();
        public readonly FakePulseSender Pulse = new FakePulseSender();
        public readonly FakeGovernor Governor = new FakeGovernor();
        public readonly FakePlacement Placement = new FakePlacement();
        public readonly List<string> Log = new List<string>();
        public readonly Session Session;

        public Harness()
        {
            Window = new FakeWindow(topLevel: true);
            Locator.Next = GameWindow.Find(Process.GetCurrentProcess())
                ?? throw new InvalidOperationException("测试窗口未被找到");
            Session = new Session(
                new PulseRecipe { Keys = new[] { 0x10 } },
                Locator,
                Pulse,
                _ => Governor,
                Placement,
                Log.Add)
            {
                IntervalSec = 5,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            Window.Dispose();
        }
    }

    [Fact]
    public async Task S01_Start_AppliesPolicyOnceAndEntersRunning()
    {
        await using var h = new Harness();
        Assert.True(await h.Session.AttachAsync());
        Assert.Equal(SessionState.Ready, h.Session.State);

        await h.Session.StartAsync();

        Assert.Equal(SessionState.Running, h.Session.State);
        Assert.Equal(1, h.Governor.ApplyCalls);
    }

    [Fact]
    public async Task S02_Stop_CancelsSchedulerRestoresResourcesAndFinalizes()
    {
        await using var h = new Harness();
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        await h.Session.ToggleOffscreenAsync();

        await h.Session.StopAsync();

        Assert.Equal(1, h.Governor.RestoreCalls);
        Assert.Equal(1, h.Placement.RestoreCalls);
        Assert.Equal(SessionState.Ready, h.Session.State);
        Assert.False(h.Session.IsRunning);
    }

    [Fact]
    public async Task S03_StartTwice_SecondIsNoOp()
    {
        await using var h = new Harness();
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        await h.Session.StartAsync();

        Assert.Equal(1, h.Governor.ApplyCalls);
    }

    [Fact]
    public async Task S04_StopTwice_IsIdempotent()
    {
        await using var h = new Harness();
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        await h.Session.StopAsync();
        await h.Session.StopAsync();

        Assert.Equal(1, h.Governor.RestoreCalls);
    }

    [Fact]
    public async Task S10_DisposeFromRunning_CleansUpExactlyOnce()
    {
        await using var h = new Harness();
        await h.Session.AttachAsync();
        await h.Session.StartAsync();

        await h.Session.DisposeAsync();

        Assert.Equal(SessionState.Disposed, h.Session.State);
        Assert.Equal(1, h.Governor.RestoreCalls);
    }

    [Fact]
    public async Task S05_TargetDies_ReattachesToNewTargetAndReappliesPolicy()
    {
        await using var h = new Harness();
        h.Session.ReattachPollMs = 50;
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        Assert.Equal(1, h.Governor.ApplyCalls);

        using var standby = new FakeWindow(topLevel: true);
        h.Locator.Next = null;
        h.Window.Dispose();

        Assert.True(await WaitFor(() => h.Session.State == SessionState.Reattaching, 10000));

        h.Locator.Next = GameWindow.Find(Process.GetCurrentProcess());
        Assert.NotNull(h.Locator.Next);
        Assert.True(await WaitFor(() => h.Session.State == SessionState.Running, 10000));
        Assert.Equal(2, h.Governor.ApplyCalls);
        Assert.Equal(1, h.Governor.RestoreCalls);
    }

    [Fact]
    public async Task S07_ManualReattach_PreservesPlacementSnapshot()
    {
        await using var h = new Harness();
        h.Session.ReattachPollMs = 50;
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        await h.Session.ToggleOffscreenAsync();
        Assert.True(h.Placement.IsOffscreen);

        h.Locator.Next = GameWindow.Find(Process.GetCurrentProcess());
        Assert.NotNull(h.Locator.Next);
        await h.Session.ReattachAsync();

        Assert.True(await WaitFor(() => h.Placement.RestoreCalls == 1, 10000));
        Assert.True(await WaitFor(() => h.Session.State == SessionState.Running, 10000));
        Assert.False(h.Placement.IsOffscreen);
    }

    [Fact]
    public async Task S08_PulseFailuresWithInvalidTarget_Reattaches()
    {
        await using var h = new Harness();
        h.Session.ReattachPollMs = 50;
        h.Pulse.Fail = true;
        await h.Session.AttachAsync();
        await h.Session.StartAsync();

        using var standby = new FakeWindow(topLevel: true);
        h.Locator.Next = null;
        h.Window.Dispose();

        Assert.True(await WaitFor(() => h.Session.State == SessionState.Reattaching, 10000));

        h.Locator.Next = GameWindow.Find(Process.GetCurrentProcess());
        Assert.NotNull(h.Locator.Next);
        Assert.True(await WaitFor(() => h.Session.State == SessionState.Running, 10000));
    }

    [Fact]
    public async Task S09_PartialPolicyFailure_KeepsRunningWithVisibleError()
    {
        await using var h = new Harness();
        h.Governor.PriorityFails = true;
        await h.Session.AttachAsync();

        await h.Session.StartAsync();

        Assert.Equal(SessionState.Running, h.Session.State);
        Assert.Contains(h.Log, line => line.Contains("失败"));
        Assert.Equal(SessionNoticeKind.ResourcePartialFailure, h.Session.DequeueNotice()?.Kind);
    }

    [Fact]
    public async Task S11_Reattach_EmitsNoticeWithPidTransition()
    {
        await using var h = new Harness();
        h.Session.ReattachPollMs = 50;
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        int oldPid = (int)h.Session.Target!.Pid;

        using var standby = new FakeWindow(topLevel: true);
        h.Locator.Next = null;
        h.Window.Dispose();
        Assert.True(await WaitFor(() => h.Session.State == SessionState.Reattaching, 10000));

        h.Locator.Next = GameWindow.Find(Process.GetCurrentProcess());
        Assert.NotNull(h.Locator.Next);
        Assert.True(await WaitFor(() => h.Session.State == SessionState.Running, 10000));

        SessionNotice? notice = h.Session.DequeueNotice();
        Assert.NotNull(notice);
        Assert.Equal(SessionNoticeKind.TargetReattached, notice.Kind);
        Assert.Equal(oldPid, notice.OldPid);
    }

    [Fact]
    public async Task S12_BossKeyWithoutTarget_ReportsInsteadOfDoingNothing()
    {
        await using var h = new Harness();
        h.Locator.Next = null;

        await h.Session.ToggleOffscreenAsync();

        Assert.Equal(SessionState.WaitingForTarget, h.Session.State);
        Assert.Equal(SessionNoticeKind.GameNotFound, h.Session.DequeueNotice()?.Kind);
        Assert.Contains(h.Log, line => line.Contains("《守望先锋》"));
        Assert.Equal(0, h.Placement.MoveCalls);
    }

    [Fact]
    public async Task S13_BossKeyWithoutTarget_AttachesWhenGameAppears()
    {
        await using var h = new Harness();
        h.Locator.Next = null;
        await h.Session.ToggleOffscreenAsync();
        Assert.Equal(0, h.Placement.MoveCalls);

        using var standby = new FakeWindow(topLevel: true);
        h.Locator.Next = GameWindow.Find(Process.GetCurrentProcess());
        Assert.NotNull(h.Locator.Next);

        await h.Session.ToggleOffscreenAsync();

        Assert.Equal(1, h.Placement.MoveCalls);
        Assert.True(h.Placement.StyleRestorePending);
    }

    [Fact]
    public async Task S15_HotApply_RetimesWaitAndUsesNewKeys()
    {
        await using var h = new Harness();
        h.Session.IntervalSec = 60;
        await h.Session.AttachAsync();
        await h.Session.StartAsync();
        Assert.True(await WaitFor(() => h.Pulse.Calls >= 1, 5000), "第一次脉冲应立即发出");

        var updated = new AppConfig();
        updated.Input.Keys = new List<string> { "e" };
        updated.Input.IntervalSeconds = 5;
        await h.Session.ApplyConfigAsync(updated);

        Assert.True(await WaitFor(() => h.Pulse.Calls >= 2, 12000), "间隔改为 5 秒后当前等待应重新计时");
        Assert.Equal(KeyNames.Parse("e"), h.Pulse.LastRecipe!.Keys[0]);
        Assert.Equal(5, h.Session.IntervalSec);
    }

    [Fact]
    public async Task S16_HotApply_WhenIdle_OnlyUpdatesState()
    {
        var session = new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            new FakeLocator(),
            new FakePulseSender(),
            _ => new FakeGovernor(),
            new FakePlacement(),
            _ => { });

        var updated = new AppConfig();
        updated.Input.IntervalSeconds = 45;
        updated.Input.Keys = new List<string> { "e" };

        ResourceApplyResult? applied = await session.ApplyConfigAsync(updated);

        Assert.Null(applied);
        Assert.Equal(45, session.IntervalSec);
        Assert.Equal(KeyNames.Parse("e"), session.Recipe.Keys[0]);
    }

    [Fact]
    public async Task S17_ToggleOffscreen_RestoresWithActivation()
    {
        await using var h = new Harness();
        await h.Session.AttachAsync();
        await h.Session.ToggleOffscreenAsync();
        Assert.True(h.Placement.IsOffscreen);

        await h.Session.ToggleOffscreenAsync();

        Assert.Equal(true, h.Placement.LastRestoreActivate);
        Assert.False(h.Placement.NeedsRestore);
    }

    [Fact]
    public async Task S18_ConsecutiveFailuresWithDeadTarget_ReattachesWithoutWaitingFullInterval()
    {
        await using var h = new Harness();
        h.Session.IntervalSec = 5;
        h.Session.ReattachPollMs = 50;
        h.Pulse.Fail = true;
        h.Pulse.OnExecute = () =>
        {
            if (h.Pulse.Calls == 2)
            {
                h.Session.IntervalSec = 60;
                h.Window.Dispose();
            }
        };
        await h.Session.AttachAsync();
        await h.Session.StartAsync();

        Assert.True(
            await WaitFor(() => h.Session.State == SessionState.Reattaching, 15000),
            "连续失败且目标已失效时应不等满间隔直接重连");
    }

    [Fact]
    public async Task S19_PartialRestore_LogsRestorePartialEvent()
    {
        string logPath = Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"), "test.log");
        var log = new AppLog(logPath, LogLevel.Debug);
        using var window = new FakeWindow(topLevel: true);
        var locator = new FakeLocator { Next = GameWindow.Find(Process.GetCurrentProcess()) };
        var governor = new FakeGovernor { RestoreFails = true };
        var session = new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            locator,
            new FakePulseSender(),
            _ => governor,
            new FakePlacement(),
            _ => { },
            log);
        Assert.NotNull(locator.Next);

        await session.AttachAsync();
        await session.StartAsync();
        await session.StopAsync();
        await session.DisposeAsync();

        Assert.Contains(log.Tail(50), line => line.Contains("RESOURCE_RESTORE_PARTIAL"));
    }

    [Fact]
    public void S14_ApplyConfig_MapsEveryField()
    {
        var config = new AppConfig();
        config.Input.Keys = new List<string> { "mouseleft", "shift" };
        config.Input.IntervalSeconds = 45;
        config.Input.JitterPercent = 7;
        config.Input.HoldMilliseconds = 150;
        config.Input.FocusWaitMilliseconds = 30;
        config.Input.SkipWhenTargetForeground = false;
        config.Resource.Priority = "AboveNormal";
        config.Resource.EcoQoS = false;
        config.Window.AllowMoveOffscreen = false;
        config.Window.KeepOffscreenAcrossRestart = true;
        config.Target.ProcessName = "Notepad";

        var session = new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            new FakeLocator(),
            new FakePulseSender(),
            _ => new FakeGovernor(),
            new FakePlacement(),
            _ => { });

        session.ApplyConfig(config);

        Assert.Equal(45, session.IntervalSec);
        Assert.Equal(7, session.JitterPercent);
        Assert.Equal("Notepad", session.TargetProcessName);
        Assert.False(session.SkipWhenForeground);
        Assert.False(session.AllowMoveOffscreen);
        Assert.True(session.KeepOffscreenAcrossRestart);
        Assert.Equal(ProcessPriorityClass.AboveNormal, session.Policy.Priority);
        Assert.False(session.Policy.EcoQoS);
        Assert.Equal(new[] { 0x01, 0x10 }, session.Recipe.Keys.ToArray());
        Assert.Equal(150, session.Recipe.HoldMs);
        Assert.Equal(30, session.Recipe.FocusWaitMs);
    }

    static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }
}




