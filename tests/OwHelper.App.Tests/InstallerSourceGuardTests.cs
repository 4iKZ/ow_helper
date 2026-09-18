using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OwHelper.App.Tests;

public class InstallerSourceGuardTests : IDisposable
{
    readonly string testDir;

    public InstallerSourceGuardTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "OwHelper_InstallerSourceGuardTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    public static readonly string[] ForbiddenFiles = new[]
    {
        "BgKeyProbe.exe",
        "BgKeyProbe.dll",
        "BgKeyProbe.deps.json",
        "BgKeyProbe.runtimeconfig.json",
        "OwHelper.exe",
        "OwHelper.deps.json",
        "OwHelper.runtimeconfig.json"
    };

    public static IReadOnlyList<string> ValidateInstallerSource(string directory)
    {
        var violations = new List<string>();
        if (!Directory.Exists(directory))
        {
            violations.Add("Directory does not exist.");
            return violations;
        }

        string desktopExe = Path.Combine(directory, "OwHelper.Desktop.exe");
        if (!File.Exists(desktopExe))
        {
            violations.Add("Missing required entry executable: OwHelper.Desktop.exe");
        }

        foreach (string forbidden in ForbiddenFiles)
        {
            if (File.Exists(Path.Combine(directory, forbidden)))
            {
                violations.Add($"Forbidden artifact found in installer source: {forbidden}");
            }
        }

        return violations;
    }

    [Fact]
    public void ValidateInstallerSource_WhenPureDesktopSource_PassesValidation()
    {
        File.WriteAllText(Path.Combine(testDir, "OwHelper.Desktop.exe"), "dummy");
        File.WriteAllText(Path.Combine(testDir, "OwHelper.Desktop.dll"), "dummy");
        File.WriteAllText(Path.Combine(testDir, "OwHelper.App.dll"), "dummy");
        File.WriteAllText(Path.Combine(testDir, "OwHelper.Core.dll"), "dummy");

        IReadOnlyList<string> violations = ValidateInstallerSource(testDir);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("BgKeyProbe.exe")]
    [InlineData("BgKeyProbe.dll")]
    [InlineData("BgKeyProbe.deps.json")]
    [InlineData("OwHelper.exe")]
    [InlineData("OwHelper.deps.json")]
    public void ValidateInstallerSource_WhenForbiddenFilePresent_FailsValidation(string forbiddenFile)
    {
        File.WriteAllText(Path.Combine(testDir, "OwHelper.Desktop.exe"), "dummy");
        File.WriteAllText(Path.Combine(testDir, forbiddenFile), "forbidden");

        IReadOnlyList<string> violations = ValidateInstallerSource(testDir);

        Assert.Contains(violations, v => v.Contains(forbiddenFile));
    }

    [Fact]
    public void ValidateInstallerSource_WhenMissingDesktopExe_FailsValidation()
    {
        IReadOnlyList<string> violations = ValidateInstallerSource(testDir);

        Assert.Contains(violations, v => v.Contains("OwHelper.Desktop.exe"));
    }
}
