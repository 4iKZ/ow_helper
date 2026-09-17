using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public PulseResult Execute(IntPtr hwnd, PulseRecipe recipe)
        {
            Calls++;
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
            return new ResourceRestoreResult(Ok("Priority"), Ok("Power"));
        }

        static OperationResult Ok(string name) => new OperationResult(name, true, null, "ok");
    }

    sealed class FakePlacement : IWindowPlacementController
    {
        public int MoveCalls;
        public int RestoreCalls;

        public bool IsOffscreen { get; private set; }

        public WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid)
        {
            MoveCalls++;
            IsOffscreen = true;
            return new WindowPlacementResult(true, "moved", null);
        }

        public WindowPlacementResult Restore()
        {
            RestoreCalls++;
            IsOffscreen = false;
            return new WindowPlacementResult(true, "restored", null);
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


