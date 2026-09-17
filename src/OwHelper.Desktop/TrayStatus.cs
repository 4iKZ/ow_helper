using System;
using OwHelper;

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
