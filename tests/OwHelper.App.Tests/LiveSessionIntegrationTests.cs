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

[Collection("standin")]
public class LiveSessionIntegrationTests
{
    static Session CreateSession(string processName, List<string> log, RuntimeStateStore? store = null)
        => new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            log.Add,
            null,
            store)
        {
            TargetProcessName = processName,
            IntervalSec = 5,
            ReattachPollMs = 50,
        };

    static string Dump(List<string> log) => " | log: " + string.Join(" || ", log);

    static ProcessPriorityClass ReadPriority(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.PriorityClass;
    }

    [Fact(Timeout = 60000)]
    public async Task Live_PriorityRoundTrip_RestoresOriginalAboveNormal()
    {
        var log = new List<string>();
        using var stand = StandInProcess.Start();
        stand.Process.PriorityClass = ProcessPriorityClass.AboveNormal;
        await using var session = CreateSession(StandInProcess.Name, log);
        Assert.True(await session.AttachAsync(), "attach failed" + Dump(log));
        Assert.True(session.Target!.Validate().Valid, "validate: " + session.Target.Validate().Reason + Dump(log));

        await session.StartAsync();
        Assert.True(ReadPriority(stand.Process.Id) == ProcessPriorityClass.BelowNormal,
            "priority after start = " + ReadPriority(stand.Process.Id) + Dump(log));

        await session.StopAsync();
        Assert.True(ReadPriority(stand.Process.Id) == ProcessPriorityClass.AboveNormal,
            "priority after stop = " + ReadPriority(stand.Process.Id) + Dump(log));
    }

    [Fact(Timeout = 60000)]
    public async Task Live_RestartTarget_AutoReattachesAndReappliesPolicy()
    {
        var log = new List<string>();
        using var first = StandInProcess.Start();
        await using var session = CreateSession(StandInProcess.Name, log);
        Assert.True(await session.AttachAsync(), "attach failed" + Dump(log));
        await session.StartAsync();
        Assert.True(ReadPriority(first.Process.Id) == ProcessPriorityClass.BelowNormal,
            "first priority = " + ReadPriority(first.Process.Id) + Dump(log));

        first.Kill();
        Assert.True(await WaitFor(() => session.State == SessionState.Reattaching, 15000), "not reattaching" + Dump(log));

        using var second = StandInProcess.Start();
        Assert.True(await WaitFor(() => session.State == SessionState.Running, 15000), "not running" + Dump(log));
        Assert.True(ReadPriority(second.Process.Id) == ProcessPriorityClass.BelowNormal,
            "second priority = " + ReadPriority(second.Process.Id) + Dump(log));

        await session.StopAsync();
        Assert.True(ReadPriority(second.Process.Id) == ProcessPriorityClass.Normal,
            "second priority after stop = " + ReadPriority(second.Process.Id) + Dump(log));
    }

    [Fact(Timeout = 60000)]
    public async Task Live_OffscreenThenManualReattach_RestoresWindow()
    {
        var log = new List<string>();
        using var stand = StandInProcess.Start();
        var store = new RuntimeStateStore(Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"), "runtime-state.json"));
        await using var session = CreateSession(StandInProcess.Name, log, store);
        Assert.True(await session.AttachAsync(), "attach failed" + Dump(log));
        await session.StartAsync();

        GameWindow target = session.Target!;
        var original = WindowProbe.GetRect(target.Handle);
        long originalExStyle = WindowProbe.GetExStyle(target.Handle);

        await session.ToggleOffscreenAsync();
        Assert.True(WindowProbe.GetRect(target.Handle).Left == -10000, "not offscreen" + Dump(log));
        Assert.True((WindowProbe.GetExStyle(target.Handle) & 0x00000080L) != 0, "not hidden from taskbar" + Dump(log));
        Assert.NotNull(store.Load());

        await session.ReattachAsync();

        Assert.True(await WaitFor(() => WindowProbe.GetRect(target.Handle).Left == original.Left, 15000), "not restored" + Dump(log));
        Assert.True(await WaitFor(() => WindowProbe.GetExStyle(target.Handle) == originalExStyle, 15000), "taskbar style not restored" + Dump(log));
        Assert.True(await WaitFor(() => session.State == SessionState.Running, 15000), "not running" + Dump(log));
        Assert.Null(store.Load());
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
