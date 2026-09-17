using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OwHelper;
using Xunit;

namespace OwHelper.App.Tests;

public class AppConfigTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"));

    static string WriteConfig(string json)
    {
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaults()
    {
        string path = Path.Combine(TempDir(), "config.json");

        var config = AppConfig.Load(path, out var problems);

        Assert.Empty(problems);
        Assert.True(File.Exists(path));
        Assert.Equal(30, config.Input.IntervalSeconds);
        Assert.Equal(0, config.Input.JitterPercent);
        Assert.Equal("Overwatch", config.Target.ProcessName);
    }

    [Fact]
    public void Load_InvalidJson_KeepsFileAndFallsBackToDefaults()
    {
        string original = "{ this is not json";
        string path = WriteConfig(original);

        var config = AppConfig.Load(path, out var problems);

        Assert.Contains(problems, p => p.Contains("解析失败"));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(30, config.Input.IntervalSeconds);
    }

    [Fact]
    public void Load_ClampsOutOfRangeValues()
    {
        string path = WriteConfig("{\"input\":{\"intervalSeconds\":999,\"jitterPercent\":-5,\"holdMilliseconds\":5}}");

        var config = AppConfig.Load(path, out var problems);

        Assert.Equal(300, config.Input.IntervalSeconds);
        Assert.Equal(0, config.Input.JitterPercent);
        Assert.Equal(10, config.Input.HoldMilliseconds);
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void Load_UnknownKey_FallsBackToShift()
    {
        string path = WriteConfig("{\"input\":{\"keys\":[\"shift\",\"notakey\"]}}");

        var config = AppConfig.Load(path, out var problems);

        Assert.Contains(problems, p => p.Contains("notakey"));
        Assert.Equal(new[] { "shift" }, config.Input.Keys.ToArray());
    }

    [Fact]
    public void Load_UnknownFields_AreIgnored()
    {
        string path = WriteConfig("{\"mysterySection\":{\"x\":1},\"input\":{\"intervalSeconds\":45}}");

        var config = AppConfig.Load(path, out var problems);

        Assert.Empty(problems);
        Assert.Equal(45, config.Input.IntervalSeconds);
    }

    [Fact]
    public void Load_InvalidPriority_FallsBackToBelowNormal()
    {
        string path = WriteConfig("{\"resource\":{\"priority\":\"Turbo\"}}");

        var config = AppConfig.Load(path, out var problems);

        Assert.Contains(problems, p => p.Contains("Turbo"));
        Assert.Equal(ProcessPriorityClass.BelowNormal, config.BuildPolicy().Priority);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        string path = Path.Combine(TempDir(), "config.json");
        var config = new AppConfig();
        config.Input.IntervalSeconds = 45;
        config.Input.Keys = new List<string> { "shift", "w" };
        config.Window.KeepOffscreenAcrossRestart = true;
        config.Save(path);

        var loaded = AppConfig.Load(path, out var problems);

        Assert.Empty(problems);
        Assert.Equal(45, loaded.Input.IntervalSeconds);
        Assert.Equal(new[] { "shift", "w" }, loaded.Input.Keys.ToArray());
        Assert.True(loaded.Window.KeepOffscreenAcrossRestart);
    }

    [Fact]
    public void BuildRecipe_MapsKeyNamesToVirtualKeys()
    {
        var config = new AppConfig();
        config.Input.Keys = new List<string> { "shift", "w" };

        var recipe = config.BuildRecipe();

        Assert.Equal(new[] { 0x10, 0x57 }, recipe.Keys.ToArray());
    }

    [Fact]
    public void DeleteOlderThan_RemovesOnlyOldLogs()
    {
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        string oldFile = Path.Combine(dir, "owhelper-20200101.log");
        string newFile = Path.Combine(dir, "owhelper-20990101.log");
        File.WriteAllText(oldFile, "x");
        File.WriteAllText(newFile, "y");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

        int deleted = AppLog.DeleteOlderThan(dir, 7);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }
}
