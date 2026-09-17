using System.Diagnostics;

namespace OwHelper.Core;

public sealed record ResourcePolicy(ProcessPriorityClass Priority, bool EcoQoS)
{
    public static ResourcePolicy Default { get; } = new ResourcePolicy(ProcessPriorityClass.BelowNormal, true);
}
