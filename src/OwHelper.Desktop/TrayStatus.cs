using System;
using OwHelper;

namespace OwHelper.Desktop;

public sealed record TrayStatus(
    SessionState State,
    bool TargetAlive,
    bool Minimized,
    int? Pid,
    int Width,
    int Height,
    int IntervalSec,
    int PulseCount,
    DateTimeOffset? LastPulseAt,
    bool ResourcePartialFailure);
