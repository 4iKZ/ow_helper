using System;

namespace OwHelper.Core;

public static class Schedule
{
    public static int NextWaitMs(int intervalSec, Random rng)
    {
        double next = intervalSec * (0.85 + rng.NextDouble() * 0.3);
        return Math.Max(1000, (int)(next * 1000));
    }
}
