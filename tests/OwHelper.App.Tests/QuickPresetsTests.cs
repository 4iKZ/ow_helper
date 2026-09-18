using System;
using OwHelper;
using OwHelper.Desktop;
using Xunit;

namespace OwHelper.App.Tests;

public class QuickPresetsTests
{
    [Fact]
    public void TorbjornPass_UsesShiftEveryThirtySeconds()
    {
        QuickPreset preset = QuickPresets.TorbjornPass;

        Assert.Equal(new[] { "shift" }, preset.Keys);
        Assert.Equal(30, preset.IntervalSeconds);
        Assert.Equal(200, preset.HoldMilliseconds);
        Assert.True(preset.SkipWhenForeground);
        Assert.Equal(0, preset.JitterPercent);
    }

    [Fact]
    public void TorbjornPass_KeyNamesResolveToShiftVirtualKey()
    {
        int vk = OwHelper.Core.KeyNames.Parse(QuickPresets.TorbjornPass.Keys[0]);

        Assert.Equal(0x10, vk);
    }

    [Fact]
    public void Apply_WritesPresetIntoConfig()
    {
        var config = new AppConfig();
        config.Input.Keys = new System.Collections.Generic.List<string> { "mouseleft" };
        config.Input.IntervalSeconds = 5;

        QuickPresets.Apply(QuickPresets.TorbjornPass, config);

        Assert.Equal(new[] { "shift" }, config.Input.Keys);
        Assert.Equal(30, config.Input.IntervalSeconds);
        Assert.Equal(200, config.Input.HoldMilliseconds);
        Assert.True(config.Input.SkipWhenTargetForeground);
        Assert.Equal(0, config.Input.JitterPercent);
    }

    [Fact]
    public void Apply_ProducesValidRecipe()
    {
        var config = new AppConfig();

        QuickPresets.Apply(QuickPresets.TorbjornPass, config);

        Assert.Empty(config.Validate());
        Assert.Equal(new[] { 0x10 }, config.BuildRecipe().Keys);
    }

    [Fact]
    public void ToConfig_AppliesPresetOnTopOfDefaults()
    {
        var config = QuickPresets.ToConfig(QuickPresets.TorbjornPass);

        Assert.Equal(new[] { "shift" }, config.Input.Keys);
        Assert.Equal(30, config.Input.IntervalSeconds);
        Assert.Equal("Overwatch", config.Target.ProcessName);
        Assert.Equal("BelowNormal", config.Resource.Priority);
    }

    [Fact]
    public void Titles_AvoidJargon()
    {
        foreach (string text in new[] { QuickPresets.TorbjornPass.Title, QuickPresets.TorbjornPass.Subtitle })
        {
            Assert.DoesNotContain("PID", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("脉冲", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EcoQoS", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
