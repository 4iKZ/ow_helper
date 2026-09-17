using System;
using System.Drawing;
using OwHelper.Core;

namespace OwHelper.Desktop;

public sealed record TrayStatus(
    SessionState State,
    bool TargetAlive,
    int? Pid,
    nint Hwnd,
    int Width,
    int Height,
    int IntervalSec,
    int JitterPercent,
    int PulseCount,
    DateTimeOffset? LastPulseAt,
    string LogPath,
    string ConfigPath,
    string GpuSummary,
    string GpuGuidance,
    bool ResourcePartialFailure);

public static class TrayStatusMapper
{
    public static Color StatusColor(TrayStatus status)
    {
        if (status.State == SessionState.Running && status.TargetAlive && !status.ResourcePartialFailure)
        {
            return Palette.StatusRunning;
        }
        return status.State switch
        {
            SessionState.Running or SessionState.Starting or SessionState.Stopping or
            SessionState.Reattaching or SessionState.WaitingForTarget => Palette.StatusWaiting,
            SessionState.Faulted => Palette.StatusFaulted,
            _ => Palette.StatusStopped,
        };
    }

    public static string StateLabel(SessionState state) => state switch
    {
        SessionState.Detached => "未绑定",
        SessionState.WaitingForTarget => "等待游戏",
        SessionState.Ready => "就绪",
        SessionState.Starting => "启动中",
        SessionState.Running => "运行中",
        SessionState.Reattaching => "重连中",
        SessionState.Stopping => "停止中",
        SessionState.Faulted => "故障",
        SessionState.Disposed => "已退出",
        _ => state.ToString(),
    };

    public static string Headline(TrayStatus status)
    {
        string state = StateLabel(status.State);
        if (status.State == SessionState.Running && !status.TargetAlive)
        {
            state = "目标失联";
        }
        else if (status.State == SessionState.Running && status.ResourcePartialFailure)
        {
            state = "运行中（资源策略部分失败）";
        }

        if (status.Pid is int pid)
        {
            string pulse = status.LastPulseAt is DateTimeOffset at ? $" · {Ago(at)}" : "";
            return $"{state} · PID {pid}{pulse}";
        }
        return state;
    }

    public static string Ago(DateTimeOffset at)
    {
        TimeSpan delta = DateTimeOffset.Now - at;
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds} 秒前脉冲";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} 分钟前脉冲";
        return $"{(int)delta.TotalHours} 小时前脉冲";
    }
}

