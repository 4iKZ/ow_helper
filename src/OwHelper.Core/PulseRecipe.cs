using System.Collections.Generic;

namespace OwHelper.Core;

public sealed class PulseRecipe
{
    public IReadOnlyList<int> Keys { get; init; } = new List<int>();
    public bool SendActivateApp { get; init; }
    public bool SendActivate { get; init; }
    public bool SendFocus { get; init; } = true;
    public int FocusWaitMs { get; init; } = 50;
    public int HoldMs { get; init; } = 200;

    public static PulseRecipe Default { get; } = new PulseRecipe { Keys = new[] { 0x10 } };
}
