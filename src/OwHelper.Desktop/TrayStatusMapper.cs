using System.Drawing;
using OwHelper;

namespace OwHelper.Desktop;

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

    public static string TrayText(TrayStatus status)
    {
        string state = PlainLanguage.RunState(status);
        string pulses = status.PulseCount > 0 ? $" · 已按键 {status.PulseCount} 次" : "";
        return Trim($"OW 助手 · {state}{pulses}", 63);
    }

    static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
