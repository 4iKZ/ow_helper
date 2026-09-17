using System;
using OwHelper;

namespace OwHelper.Desktop;

public static class PlainLanguage
{
    public static string Connection(TrayStatus status)
    {
        if (status.Pid == null) return "还没有找到《守望先锋》";
        if (!status.TargetAlive) return "游戏好像关掉了";
        return "已连接《守望先锋》";
    }

    public static string RunState(TrayStatus status) => status.State switch
    {
        SessionState.WaitingForTarget => "等你打开《守望先锋》",
        SessionState.Detached => "未开始",
        SessionState.Ready => "准备好了，点下面的按钮开始",
        SessionState.Starting => "正在开始…",
        SessionState.Running => status.TargetAlive ? "正在挂机" : "游戏关掉了，正在等待",
        SessionState.Reattaching => "正在重新连接游戏…",
        SessionState.Stopping => "正在停止…",
        SessionState.Faulted => "出了点问题，请重新打开本程序",
        SessionState.Disposed => "已退出",
        _ => "未开始",
    };

    public static string PulseSummary(TrayStatus status) => $"已自动按键 {status.PulseCount} 次";

    public static string NextPulse(TrayStatus status)
    {
        if (status.State != SessionState.Running || status.LastPulseAt is not DateTimeOffset last)
        {
            return "";
        }
        int remaining = (int)Math.Round(status.IntervalSec - (DateTimeOffset.Now - last).TotalSeconds);
        if (remaining < 0) remaining = 0;
        return $"下次按键：约 {remaining} 秒后";
    }

    public static string WindowInfo(TrayStatus status, bool offscreen)
    {
        if (offscreen) return "游戏窗口：已隐藏（点「恢复游戏窗口」可显示）";
        if (status.Minimized) return "游戏窗口：已最小化（不影响挂机）";
        if (status.Pid is int && status.Width > 0) return $"游戏窗口：{status.Width}×{status.Height}";
        return "";
    }

    public static string ResourceNote(TrayStatus status)
        => status.ResourcePartialFailure ? "后台省电模式没有生效（不影响挂机）" : "";

    public static string Notice(SessionNotice notice) => notice.Kind switch
    {
        SessionNoticeKind.TargetLost => "《守望先锋》已关闭，挂机已停止",
        SessionNoticeKind.TargetReattached => "游戏重新打开了，已自动重新连接",
        SessionNoticeKind.ResourcePartialFailure => "后台省电模式没有生效（不影响挂机）",
        SessionNoticeKind.PulseFailures => "按键发送失败了几次，可能是权限问题（试试用管理员身份打开本程序）",
        SessionNoticeKind.GameNotFound => "还没有找到《守望先锋》，请先打开游戏（打开后会自动连接）",
        _ => "有新的运行提示",
    };


}

