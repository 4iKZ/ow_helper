using System.Diagnostics;
using Xunit;

namespace OwHelper.Core.Tests;

public class ResourceGovernorTests
{
    [Fact]
    public void ApplyAndRestore_RoundTripsPriorityOnOwnProcess()
    {
        using var self = Process.GetCurrentProcess();
        var governor = new ResourceGovernor(self);
        try
        {
            governor.Apply();
            Assert.Equal(ProcessPriorityClass.BelowNormal, self.PriorityClass);
        }
        finally
        {
            governor.Restore();
        }
        Assert.Equal(ProcessPriorityClass.Normal, self.PriorityClass);
    }

    [Fact]
    public void Restore_IsIdempotent()
    {
        using var self = Process.GetCurrentProcess();
        var governor = new ResourceGovernor(self);
        governor.Restore();
        governor.Restore();
        Assert.Equal(ProcessPriorityClass.Normal, self.PriorityClass);
    }
}
