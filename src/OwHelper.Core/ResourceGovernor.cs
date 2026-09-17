using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OwHelper.Core;

public sealed class ResourceGovernor
{
    readonly Process process;

    public ResourceGovernor(Process process) => this.process = process;

    public void Apply()
    {
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        SetEcoQoS(true);
    }

    public void Restore()
    {
        SetEcoQoS(false);
        try { process.PriorityClass = ProcessPriorityClass.Normal; } catch { }
    }

    void SetEcoQoS(bool enable)
    {
        try
        {
            var state = new Native.PROCESS_POWER_THROTTLING_STATE
            {
                Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enable ? Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u,
            };
            Native.SetProcessInformation(
                process.Handle,
                Native.ProcessInformationClass.ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<Native.PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { }
    }
}
