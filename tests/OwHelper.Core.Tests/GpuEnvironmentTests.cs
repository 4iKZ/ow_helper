using System.Collections.Generic;
using OwHelper.Core;
using Xunit;

namespace OwHelper.Core.Tests;

public class GpuEnvironmentTests
{
    [Fact]
    public void Detect_DoesNotThrow_AndReturnsWellFormedEntries()
    {
        IReadOnlyList<GpuInfo> gpus = GpuEnvironment.Detect();

        foreach (GpuInfo gpu in gpus)
        {
            Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
        }
    }

    [Fact]
    public void HasNvidia_MatchesVendorOrName()
    {
        var gpus = new List<GpuInfo>
        {
            new GpuInfo("Intel Corporation", "Intel(R) Iris(R) Xe Graphics"),
            new GpuInfo("NVIDIA", "NVIDIA GeForce RTX 3060 Laptop GPU"),
        };

        Assert.True(GpuEnvironment.HasNvidia(gpus));
    }

    [Fact]
    public void HasNvidia_FalseWithoutNvidia()
    {
        var gpus = new List<GpuInfo>
        {
            new GpuInfo("Intel Corporation", "Intel(R) Iris(R) Xe Graphics"),
        };

        Assert.False(GpuEnvironment.HasNvidia(gpus));
    }
}
