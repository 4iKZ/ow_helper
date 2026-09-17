using System;
using Xunit;

namespace OwHelper.App.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void SecondInstance_OnSameName_IsNotAcquired()
    {
        string name = @"Local\OwHelper.Tests." + Guid.NewGuid().ToString("N");

        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }

    [Fact]
    public void DifferentNames_AreIndependent()
    {
        using var first = new SingleInstance(@"Local\OwHelper.Tests." + Guid.NewGuid().ToString("N"));
        using var second = new SingleInstance(@"Local\OwHelper.Tests." + Guid.NewGuid().ToString("N"));

        Assert.True(first.Acquired);
        Assert.True(second.Acquired);
    }
}
