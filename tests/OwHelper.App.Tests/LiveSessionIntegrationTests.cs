using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using OwHelper;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.App.Tests;

public class LiveSessionIntegrationTests
{
    static Session CreateSession(string processName, List<string> log)
        => new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            log.Add)
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
        await using var session = CreateSession(StandInProcess.Name, log);
        Assert.True(await session.AttachAsync(), "attach failed" + Dump(log));
        await session.StartAsync();

        GameWindow target = session.Target!;
        var original = WindowProbe.GetRect(target.Handle);

        await session.ToggleOffscreenAsync();
        Assert.True(WindowProbe.GetRect(target.Handle).Left == -10000, "not offscreen" + Dump(log));

        await session.ReattachAsync();

        Assert.True(await WaitFor(() => WindowProbe.GetRect(target.Handle).Left == original.Left, 15000), "not restored" + Dump(log));
        Assert.True(await WaitFor(() => session.State == SessionState.Running, 15000), "not running" + Dump(log));
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
