using System.Collections.Generic;
using System.Linq;
using OwHelper.Tray;
using Xunit;

namespace OwHelper.App.Tests;

public class SettingsMapperTests
{
    [Fact]
    public void MergeKeys_PresetsFirstThenCustom_Deduplicates()
    {
        var keys = SettingsMapper.MergeKeys(
            new[] { "shift", "mouseleft" },
            "w, shift , mouseleft, f1");

        Assert.Equal(new[] { "shift", "mouseleft", "w", "f1" }, keys);
    }

    [Fact]
    public void MergeKeys_EmptyCustomText_ReturnsCheckedPresets()
    {
        var keys = SettingsMapper.MergeKeys(new[] { "mouseleft" }, "");

        Assert.Equal(new[] { "mouseleft" }, keys);
    }

    [Fact]
    public void MergeKeys_IgnoresBlankAndWhitespaceParts()
    {
        var keys = SettingsMapper.MergeKeys(new[] { "e" }, " , ,  , w ,");

        Assert.Equal(new[] { "e", "w" }, keys);
    }

    [Fact]
    public void CustomKeysText_ExcludesPresets_KeepsCustom()
    {
        var keys = new List<string> { "shift", "mouseleft", "f1", "numdivide" };

        string text = SettingsMapper.CustomKeysText(keys);

        Assert.Equal("f1,numdivide", text);
    }

    [Fact]
    public void CustomKeysText_AllPresets_IsEmpty()
    {
        Assert.Equal("", SettingsMapper.CustomKeysText(new[] { "shift", "mouseleft" }));
    }

    [Fact]
    public void Presets_ContainMouseButtonsAndCommonKeys()
    {
        string[] names = SettingsMapper.Presets.Select(p => p.Name).ToArray();

        Assert.Contains("mouseleft", names);
        Assert.Contains("mouseright", names);
        Assert.Contains("mousemiddle", names);
        Assert.Contains("mouse4", names);
        Assert.Contains("mouse5", names);
        Assert.Contains("shift", names);
        Assert.Contains("space", names);
        Assert.Contains("tab", names);
    }

    [Fact]
    public void IsPreset_IsCaseInsensitive()
    {
        Assert.True(SettingsMapper.IsPreset("MouseLeft"));
        Assert.False(SettingsMapper.IsPreset("f1"));
    }
}
