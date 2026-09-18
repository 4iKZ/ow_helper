using System.Collections.Generic;
using System.Linq;
using OwHelper;

namespace OwHelper.Desktop;

public sealed record QuickPreset(
    string Id,
    string Title,
    string Subtitle,
    string[] Keys,
    int IntervalSeconds,
    int HoldMilliseconds = 200,
    bool SkipWhenForeground = true,
    int JitterPercent = 0);

public static class QuickPresets
{
    public static QuickPreset TorbjornPass { get; } = new QuickPreset(
        Id: "torbjorn-pass",
        Title: "托比昂战令",
        Subtitle: "每 30 秒自动按一次 Shift",
        Keys: new[] { "shift" },
        IntervalSeconds: 30);

    public static void Apply(QuickPreset preset, AppConfig config)
    {
        config.Input.Keys = preset.Keys.ToList();
        config.Input.IntervalSeconds = preset.IntervalSeconds;
        config.Input.HoldMilliseconds = preset.HoldMilliseconds;
        config.Input.SkipWhenTargetForeground = preset.SkipWhenForeground;
        config.Input.JitterPercent = preset.JitterPercent;
    }

    public static AppConfig ToConfig(QuickPreset preset)
    {
        var config = new AppConfig();
        Apply(preset, config);
        return config;
    }
}
