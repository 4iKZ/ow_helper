using System;
using Xunit;

namespace OwHelper.Core.Tests;

public class ScheduleTests
{
    [Fact]
    public void NextWaitMs_StaysWithinJitterBounds()
    {
        var rng = new Random(12345);
        for (int i = 0; i < 10000; i++)
        {
            int ms = Schedule.NextWaitMs(30, rng);
            Assert.InRange(ms, 25500, 34500);
        }
    }

    [Fact]
    public void NextWaitMs_NeverGoesBelowFloor()
    {
        var rng = new Random(999);
        for (int i = 0; i < 10000; i++)
        {
            int ms = Schedule.NextWaitMs(2, rng);
            Assert.True(ms >= 1000);
        }
    }
}
