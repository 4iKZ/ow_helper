namespace OwHelper;

internal enum SessionState
{
    Detached,
    WaitingForTarget,
    Ready,
    Starting,
    Running,
    Reattaching,
    Stopping,
    Faulted,
    Disposed,
}
