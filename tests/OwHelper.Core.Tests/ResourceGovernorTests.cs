using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace OwHelper.Core.Tests;

public class ResourceGovernorTests
{
    [Fact]
    public void ApplyAndRestore_RoundTripsOriginalPriority()
    {
        using var self = Process.GetCurrentProcess();
        var original = self.PriorityClass;
        var governor = new ResourceGovernor(self);
        try
        {
            self.PriorityClass = ProcessPriorityClass.AboveNormal;

            var apply = governor.Apply(ResourcePolicy.Default);
            Assert.True(apply.Priority.Success);
            Assert.Equal(ProcessPriorityClass.BelowNormal, self.PriorityClass);

            var restore = governor.Restore();
            Assert.True(restore.Priority.Success);
            Assert.Equal(ProcessPriorityClass.AboveNormal, self.PriorityClass);
        }
        finally
        {
            self.PriorityClass = original;
        }
    }

    [Fact]
    public void Apply_IsIdempotent_AndKeepsFirstSnapshot()
    {
        using var self = Process.GetCurrentProcess();
        var original = self.PriorityClass;
        var governor = new ResourceGovernor(self);
        try
        {
            self.PriorityClass = ProcessPriorityClass.AboveNormal;

            governor.Apply(ResourcePolicy.Default);
            governor.Apply(ResourcePolicy.Default);
            governor.Restore();

            Assert.Equal(ProcessPriorityClass.AboveNormal, self.PriorityClass);

            var secondRestore = governor.Restore();
            Assert.True(secondRestore.Priority.Success);
            Assert.Equal(ProcessPriorityClass.AboveNormal, self.PriorityClass);
        }
        finally
        {
            self.PriorityClass = original;
        }
    }

    [Fact]
    public void Snapshot_CapturesOriginalPriority()
    {
        using var self = Process.GetCurrentProcess();
        var original = self.PriorityClass;
        var governor = new ResourceGovernor(self);
        try
        {
            self.PriorityClass = ProcessPriorityClass.AboveNormal;
            governor.Apply(ResourcePolicy.Default);

            var snapshot = governor.Snapshot;
            Assert.NotNull(snapshot);
            Assert.True(snapshot.PriorityCaptured);
            Assert.Equal(ProcessPriorityClass.AboveNormal, snapshot.PriorityClass);
        }
        finally
        {
            governor.Restore();
            self.PriorityClass = original;
        }
    }

    [Fact]
    public void Apply_ReportsStructuredResultsForBothOperations()
    {
        using var self = Process.GetCurrentProcess();
        var original = self.PriorityClass;
        var governor = new ResourceGovernor(self);
        try
        {
            var apply = governor.Apply(ResourcePolicy.Default);

            Assert.Equal("Priority", apply.Priority.Name);
            Assert.Equal("Power", apply.Power.Name);
            Assert.NotNull(apply.Priority.Message);
            Assert.NotNull(apply.Power.Message);
            if (apply.Power.Success)
            {
                Assert.Null(apply.Power.NativeError);
            }
            else
            {
                Assert.True(apply.Power.NativeError != null || apply.Power.Message != null);
            }
        }
        finally
        {
            governor.Restore();
            self.PriorityClass = original;
        }
    }

    [Fact]
    public void Restore_ReturnsPowerThrottlingToPreApplyState()
    {
        using var self = Process.GetCurrentProcess();
        if (!TryQueryPower(self, out Native.PROCESS_POWER_THROTTLING_STATE before)) return;
        var governor = new ResourceGovernor(self);
        try
        {
            ResourceApplyResult apply = governor.Apply(ResourcePolicy.Default);
            ResourceRestoreResult restore = governor.Restore();
            if (!apply.Power.Success || !restore.Power.Success) return;
            Assert.True(TryQueryPower(self, out Native.PROCESS_POWER_THROTTLING_STATE after));
            Assert.Equal(before.ControlMask, after.ControlMask);
            Assert.Equal(before.StateMask, after.StateMask);
        }
        finally
        {
            governor.Restore();
        }
    }

    static bool TryQueryPower(Process process, out Native.PROCESS_POWER_THROTTLING_STATE state)
    {
        try
        {
            return Native.GetProcessInformation(
                process.Handle,
                Native.ProcessInformationClass.ProcessPowerThrottling,
                out state,
                (uint)Marshal.SizeOf<Native.PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            state = default;
            return false;
        }
    }

    [Fact]
    public void PowerThrottlePolicy_UsesDocumentedMasks()
    {
        Assert.Equal(0x1u, PowerThrottlePolicy.ExecutionSpeed);
        Assert.Equal((1u, 1u), PowerThrottlePolicy.EcoQoS);
        Assert.Equal((0u, 0u), PowerThrottlePolicy.SystemManaged);
    }

    [Fact]
    public void Apply_OnExitedProcess_ReportsFailureWithoutThrowing()
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true });
        Assert.NotNull(process);
        process.WaitForExit();

        var governor = new ResourceGovernor(process);
        var apply = governor.Apply(ResourcePolicy.Default);

        Assert.False(apply.Success);
        Assert.False(apply.Priority.Success);
        process.Dispose();
    }
}


