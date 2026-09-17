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
            var state = new Native.PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = enable ? 1u : 0u };
            int size = Marshal.SizeOf<Native.PROCESS_POWER_THROTTLING_STATE>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(state, ptr, false);
            Native.SetProcessInformation(process.Handle, Native.PROCESS_POWER_THROTTLING, ptr, (uint)size);
            Marshal.FreeHGlobal(ptr);
        }
        catch { }
    }
}
