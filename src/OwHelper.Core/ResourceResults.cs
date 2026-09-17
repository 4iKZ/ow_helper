using System.Diagnostics;

namespace OwHelper.Core;

public static class PowerThrottlePolicy
{
    public const uint ExecutionSpeed = Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;

    public static (uint Control, uint State) EcoQoS => (ExecutionSpeed, ExecutionSpeed);

    public static (uint Control, uint State) SystemManaged => (0u, 0u);
}

public sealed record ResourceSnapshot(
    ProcessPriorityClass PriorityClass,
    bool PriorityCaptured);

public sealed record ResourceApplyResult(OperationResult Priority, OperationResult Power)
{
    public bool Success => Priority.Success && Power.Success;
}

public sealed record ResourceRestoreResult(OperationResult Priority, OperationResult Power)
{
    public bool Success => Priority.Success && Power.Success;
}
