using System;
using System.Drawing;
using OwHelper;
using OwHelper.Tray;
using Xunit;

namespace OwHelper.App.Tests;

public class TrayStatusMapperTests
{
    static TrayStatus Status(
        SessionState state,
        bool alive = true,
        bool partial = false,
        int? pid = 1234,
        DateTimeOffset? last = null)
        => new TrayStatus(state, alive, pid, (nint)0x5A81C, 1920, 1080, 30, 0, 12, last, "log.txt", "config.json", "GPU", "", partial);

    [Fact]
    public void StatusColor_RunningAlive_IsGreen()
    {
        Assert.Equal(Palette.StatusRunning, TrayStatusMapper.StatusColor(Status(SessionState.Running)));
    }

    [Fact]
    public void StatusColor_RunningWithPartialResourceFailure_IsWaiting()
    {
        Assert.Equal(Palette.StatusWaiting, TrayStatusMapper.StatusColor(Status(SessionState.Running, partial: true)));
    }

    [Fact]
    public void StatusColor_RunningWithDeadTarget_IsWaiting()
    {
        Assert.Equal(Palette.StatusWaiting, TrayStatusMapper.StatusColor(Status(SessionState.Running, alive: false)));
    }

    [Theory]
    [InlineData(SessionState.Reattaching)]
    [InlineData(SessionState.WaitingForTarget)]
    [InlineData(SessionState.Starting)]
    public void StatusColor_TransientStates_AreWaiting(SessionState state)
    {
        Assert.Equal(Palette.StatusWaiting, TrayStatusMapper.StatusColor(Status(state)));
    }

    [Fact]
    public void StatusColor_Faulted_IsFaulted()
    {
        Assert.Equal(Palette.StatusFaulted, TrayStatusMapper.StatusColor(Status(SessionState.Faulted)));
    }

    [Theory]
    [InlineData(SessionState.Ready)]
    [InlineData(SessionState.Detached)]
    public void StatusColor_IdleStates_AreStopped(SessionState state)
    {
        Assert.Equal(Palette.StatusStopped, TrayStatusMapper.StatusColor(Status(state)));
    }

    [Fact]
    public void Headline_IncludesStateAndPid()
    {
        string headline = TrayStatusMapper.Headline(Status(SessionState.Running, last: DateTimeOffset.Now.AddSeconds(-12)));

        Assert.Contains("运行中", headline);
        Assert.Contains("PID 1234", headline);
        Assert.Contains("12 秒前脉冲", headline);
    }

    [Fact]
    public void Headline_WithPartialFailure_MarksItVisible()
    {
        string headline = TrayStatusMapper.Headline(Status(SessionState.Running, partial: true));

        Assert.Contains("部分失败", headline);
    }

    [Theory]
    [InlineData(-30, "30 秒前脉冲")]
    [InlineData(-120, "2 分钟前脉冲")]
    [InlineData(-7200, "2 小时前脉冲")]
    public void Ago_FormatsRelativeTime(int secondsAgo, string expected)
    {
        Assert.Equal(expected, TrayStatusMapper.Ago(DateTimeOffset.Now.AddSeconds(secondsAgo)));
    }

    [Fact]
    public void TrayIconFactory_CreatesLoadableIcon()
    {
        using Icon icon = TrayIconFactory.Create(Palette.StatusRunning);

        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }
}
