namespace OwHelper;

public enum SessionNoticeKind
{
    TargetLost,
    TargetReattached,
    ResourcePartialFailure,
    PulseFailures,
}

public sealed record SessionNotice(
    SessionNoticeKind Kind,
    int? OldPid = null,
    int? NewPid = null);
