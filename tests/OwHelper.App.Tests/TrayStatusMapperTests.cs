using System;
using System.Drawing;
using OwHelper;
using OwHelper.Desktop;
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
        => new TrayStatus(state, alive, false, pid, 1920, 1080, 30, 12, last, partial);

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
    public void TrayText_UsesFriendlyWordsAndPulseCount()
    {
        string text = TrayStatusMapper.TrayText(Status(SessionState.Running));

        Assert.Contains("正在挂机", text);
        Assert.Contains("已按键 12 次", text);
        Assert.DoesNotContain("PID", text);
        Assert.DoesNotContain("脉冲", text);
    }

    [Fact]
    public void TrayText_IsTrimmedForWindowsTrayLimit()
    {
        string text = TrayStatusMapper.TrayText(Status(SessionState.WaitingForTarget, pid: null));

        Assert.True(text.Length <= 63);
    }

    [Fact]
    public void TrayIconFactory_CreatesLoadableIcon()
    {
        using Icon icon = TrayIconFactory.Create(Palette.StatusRunning);

        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }
}

