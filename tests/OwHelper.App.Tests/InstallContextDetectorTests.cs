using System;
using System.IO;
using Xunit;

namespace OwHelper.App.Tests;

public class InstallContextDetectorTests : IDisposable
{
    readonly string testDir;

    public InstallContextDetectorTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "OwHelper_InstallContextDetectorTests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Detect_WhenMarkerExists_IdentifiesAsInstalled()
    {
        string exePath = Path.Combine(testDir, "OwHelper.Desktop.exe");
        File.WriteAllText(exePath, "fake exe");
        File.WriteAllText(Path.Combine(testDir, InstallContextDetector.MarkerFileName), "{\"product\":\"OW Helper\"}");

        InstallContext ctx = InstallContextDetector.Detect(exePath);

        Assert.Equal(InstallMode.Installed, ctx.Mode);
        Assert.Equal(Path.GetFullPath(exePath), Path.GetFullPath(ctx.CurrentExecutablePath));
        Assert.Equal(Path.GetFullPath(testDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), ctx.CurrentDirectory);
    }

    [Fact]
    public void Detect_WhenLegacyUninstallerExists_IdentifiesAsInstalled()
    {
        string exePath = Path.Combine(testDir, "OwHelper.Desktop.exe");
        File.WriteAllText(exePath, "fake exe");
        File.WriteAllText(Path.Combine(testDir, InstallContextDetector.LegacyUninstallerFileName), "fake uninstaller");

        InstallContext ctx = InstallContextDetector.Detect(exePath);

        Assert.Equal(InstallMode.Installed, ctx.Mode);
        Assert.Equal(Path.GetFullPath(exePath), Path.GetFullPath(ctx.CurrentExecutablePath));
    }

    [Fact]
    public void Detect_WhenNeitherMarkerNorUninstallerExists_IdentifiesAsPortable()
    {
        string exePath = Path.Combine(testDir, "OwHelper.Desktop.exe");
        File.WriteAllText(exePath, "fake exe");

        InstallContext ctx = InstallContextDetector.Detect(exePath);

        Assert.Equal(InstallMode.Portable, ctx.Mode);
        Assert.Equal(Path.GetFullPath(exePath), Path.GetFullPath(ctx.CurrentExecutablePath));
        Assert.Equal(Path.GetFullPath(testDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), ctx.CurrentDirectory);
    }

    [Fact]
    public void Detect_WhenCustomDirectoryProvided_PreservesCustomDirectoryPath()
    {
        string customSubDir = Path.Combine(testDir, "Custom", "AppFolder");
        Directory.CreateDirectory(customSubDir);
        string exePath = Path.Combine(customSubDir, "OwHelper.Desktop.exe");
        File.WriteAllText(exePath, "fake exe");
        File.WriteAllText(Path.Combine(customSubDir, InstallContextDetector.MarkerFileName), "{}");

        InstallContext ctx = InstallContextDetector.Detect(exePath);

        Assert.Equal(InstallMode.Installed, ctx.Mode);
        Assert.Equal(Path.GetFullPath(customSubDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), ctx.CurrentDirectory);
    }

    [Fact]
    public void Detect_WhenNullExecutablePath_DoesNotThrowAndResolvesContext()
    {
        InstallContext ctx = InstallContextDetector.Detect(null);

        Assert.NotNull(ctx);
        Assert.False(string.IsNullOrWhiteSpace(ctx.CurrentDirectory));
        Assert.True(ctx.Mode == InstallMode.Installed || ctx.Mode == InstallMode.Portable);
    }
}
