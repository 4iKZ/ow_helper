using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OwHelper.Core;

public sealed class ResourceGovernor : IResourceGovernor
{
    readonly Process process;
    ProcessPriorityClass originalPriority;
    bool priorityCaptured;

    public ResourceGovernor(Process process) => this.process = process;

    public ResourceSnapshot? Snapshot => priorityCaptured
        ? new ResourceSnapshot(originalPriority, true)
        : null;

    public ResourceApplyResult Apply(ResourcePolicy policy)
    {
        CaptureOnce();
        OperationResult priority = SetPriority(policy.Priority, policy.Priority.ToString());
        OperationResult power;
        if (policy.EcoQoS)
        {
            (uint control, uint state) = PowerThrottlePolicy.EcoQoS;
            power = SetPowerThrottle(control, state, "EcoQoS enabled");
        }
        else
        {
            power = new OperationResult("Power", true, null, "EcoQoS disabled by policy");
        }
        return new ResourceApplyResult(priority, power);
    }

    public ResourceRestoreResult Restore()
    {
        OperationResult priority = RestorePriority();
        (uint control, uint state) = PowerThrottlePolicy.SystemManaged;
        OperationResult power = SetPowerThrottle(control, state, "restored to system-managed");
        return new ResourceRestoreResult(priority, power);
    }

    void CaptureOnce()
    {
        if (priorityCaptured) return;
        try
        {
            originalPriority = process.PriorityClass;
            priorityCaptured = true;
        }
        catch
        {
            priorityCaptured = false;
        }
    }

    OperationResult RestorePriority()
    {
        if (!priorityCaptured)
        {
            return new OperationResult("Priority", true, null, "no snapshot; left unchanged");
        }
        return SetPriority(originalPriority, $"restored to {originalPriority}");
    }

    OperationResult SetPriority(ProcessPriorityClass priority, string message)
    {
        try
        {
            process.PriorityClass = priority;
            return new OperationResult("Priority", true, null, message);
        }
        catch (Win32Exception ex)
        {
            return new OperationResult("Priority", false, ex.NativeErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return new OperationResult("Priority", false, null, ex.Message);
        }
    }

    OperationResult SetPowerThrottle(uint controlMask, uint stateMask, string message)
    {
        try
        {
            var state = new Native.PROCESS_POWER_THROTTLING_STATE
            {
                Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = controlMask,
                StateMask = stateMask,
            };
            bool ok = Native.SetProcessInformation(
                process.Handle,
                Native.ProcessInformationClass.ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<Native.PROCESS_POWER_THROTTLING_STATE>());
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                string detail = error == 1
                    ? "EcoQoS unsupported by system power policy"
                    : $"SetProcessInformation failed ({message})";
                return new OperationResult("Power", false, error, detail);
            }
            return new OperationResult("Power", true, null, message);
        }
        catch (Exception ex)
        {
            return new OperationResult("Power", false, null, ex.Message);
        }
    }
}
