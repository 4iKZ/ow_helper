using System;
using OwHelper;
using OwHelper.Desktop;
using Xunit;

namespace OwHelper.App.Tests;

public class PlainLanguageTests
{
    static TrayStatus Status(
        SessionState state,
        bool alive = true,
        int? pid = 1234,
        bool partial = false,
        DateTimeOffset? last = null,
        bool minimized = false)
        => new TrayStatus(state, alive, minimized, pid, (nint)0x5A81C, 1920, 1080, 30, 0, 12, last, "log.txt", "config.json", "GPU", "", partial);

    static void AssertNoJargon(string text)
    {
        foreach (string word in new[] { "PID", "HWND", "脉冲", "EcoQoS", "err=", "资源策略", "目标", "jitter", "focus" })
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(SessionState.WaitingForTarget, null, true, "等你打开《守望先锋》")]
    [InlineData(SessionState.Ready, 1234, true, "准备好了，点下面的按钮开始")]
    [InlineData(SessionState.Running, 1234, true, "正在挂机")]
    [InlineData(SessionState.Running, 1234, false, "游戏关掉了，正在等待")]
    [InlineData(SessionState.Reattaching, 1234, true, "正在重新连接游戏…")]
    [InlineData(SessionState.Stopping, 1234, true, "正在停止…")]
    public void RunState_UsesFriendlyText(SessionState state, int? pid, bool alive, string expected)
    {
        string text = PlainLanguage.RunState(Status(state, alive, pid));

        Assert.Equal(expected, text);
        AssertNoJargon(text);
    }

    [Fact]
    public void Connection_NoGame_InvitesUserToOpenIt()
    {
        string text = PlainLanguage.Connection(Status(SessionState.WaitingForTarget, pid: null));

        Assert.Contains("《守望先锋》", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void NextPulse_ShowsCountdownDuringRun()
    {
        string text = PlainLanguage.NextPulse(Status(SessionState.Running, last: DateTimeOffset.Now.AddSeconds(-12)));

        Assert.Contains("下次按键", text);
        Assert.Contains("秒后", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void NextPulse_EmptyWhenNotRunning()
    {
        Assert.Equal("", PlainLanguage.NextPulse(Status(SessionState.Ready)));
    }

    [Fact]
    public void PulseSummary_UsesPlainWords()
    {
        string text = PlainLanguage.PulseSummary(Status(SessionState.Running));

        Assert.Equal("已自动按键 12 次", text);
        AssertNoJargon(text);
    }

    [Theory]
    [InlineData(SessionNoticeKind.TargetLost, "挂机已停止")]
    [InlineData(SessionNoticeKind.TargetReattached, "已自动重新连接")]
    [InlineData(SessionNoticeKind.ResourcePartialFailure, "不影响挂机")]
    [InlineData(SessionNoticeKind.PulseFailures, "权限")]
    public void Notice_IsUnderstandable(SessionNoticeKind kind, string expectedFragment)
    {
        string text = PlainLanguage.Notice(new SessionNotice(kind));

        Assert.Contains(expectedFragment, text);
        AssertNoJargon(text);
    }

    [Fact]
    public void ResourceNote_MentionsHarmlessFailure()
    {
        string text = PlainLanguage.ResourceNote(Status(SessionState.Running, partial: true));

        Assert.Contains("不影响挂机", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void WindowInfo_ShowsSizeWhenWindowed()
    {
        string text = PlainLanguage.WindowInfo(Status(SessionState.Ready), offscreen: false);

        Assert.Equal("游戏窗口：1920×1080", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void WindowInfo_ExplainsMinimizedState()
    {
        string text = PlainLanguage.WindowInfo(Status(SessionState.Ready, minimized: true), offscreen: false);

        Assert.Contains("已最小化", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void WindowInfo_ExplainsHiddenState()
    {
        string text = PlainLanguage.WindowInfo(Status(SessionState.Ready), offscreen: true);

        Assert.Contains("已隐藏", text);
        Assert.DoesNotContain("1920", text);
        AssertNoJargon(text);
    }

    [Fact]
    public void StartHint_GuidesWhenNoGame()
    {
        Assert.Contains("先打开《守望先锋》", PlainLanguage.StartHint(Status(SessionState.WaitingForTarget, pid: null)));
        Assert.Contains("正常用电脑", PlainLanguage.StartHint(Status(SessionState.Ready)));
    }
}

