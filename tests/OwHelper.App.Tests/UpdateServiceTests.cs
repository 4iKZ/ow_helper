using System;
using System.IO;
using Xunit;

namespace OwHelper.App.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.0", true)]
    [InlineData("1.2.0", "v1.1.0", true)]
    [InlineData("1.1.1", "1.1.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("v1.0.9", "1.1.0", false)]
    [InlineData("", "1.1.0", false)]
    public void IsNewerVersion_ComparesCorrectly(string latest, string current, bool expected)
    {
        bool result = UpdateService.IsNewerVersion(latest, current);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("3.4.5", "3.4.5")]
    [InlineData("  v1.0.0  ", "1.0.0")]
    public void CleanVersionString_StripsPrefix(string input, string expected)
    {
        string result = UpdateService.CleanVersionString(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseReleaseJson_ValidNewerRelease_ReturnsUpdateInfo()
    {
        string json = """
        {
            "tag_name": "v1.2.0",
            "name": "OW 助手 1.2.0 发布",
            "body": "更新说明内容：修复了若干问题，优化了界面。",
            "draft": false,
            "prerelease": false,
            "html_url": "https://github.com/4iKZ/ow_helper/releases/tag/v1.2.0",
            "published_at": "2026-09-18T10:00:00Z",
            "assets": [
                {
                    "name": "OwHelper-Setup-1.2.0.exe",
                    "browser_download_url": "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
                    "size": 65432100
                },
                {
                    "name": "OwHelper-win-x64-1.2.0.zip",
                    "browser_download_url": "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-win-x64-1.2.0.zip",
                    "size": 70123400
                }
            ]
        }
        """;

        UpdateInfo? info = UpdateService.ParseReleaseJson(json, "1.1.0");

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info.Version);
        Assert.Equal("v1.2.0", info.TagName);
        Assert.Equal("OW 助手 1.2.0 发布", info.Title);
        Assert.Contains("修复了若干问题", info.ReleaseNotes);
        Assert.Equal("https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe", info.SetupDownloadUrl);
        Assert.Equal("https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-win-x64-1.2.0.zip", info.ZipDownloadUrl);
        Assert.Equal(65432100, info.SetupSizeBytes);
    }

    [Fact]
    public void ParseReleaseJson_DraftOrPrerelease_ReturnsNull()
    {
        string draftJson = """
        {
            "tag_name": "v1.3.0",
            "draft": true,
            "prerelease": false
        }
        """;

        string prereleaseJson = """
        {
            "tag_name": "v1.3.0",
            "draft": false,
            "prerelease": true
        }
        """;

        Assert.Null(UpdateService.ParseReleaseJson(draftJson, "1.1.0"));
        Assert.Null(UpdateService.ParseReleaseJson(prereleaseJson, "1.1.0"));
    }

    [Fact]
    public void ParseReleaseJson_SameOrOlderVersion_ReturnsNull()
    {
        string sameJson = """
        {
            "tag_name": "v1.1.0",
            "draft": false,
            "prerelease": false
        }
        """;

        Assert.Null(UpdateService.ParseReleaseJson(sameJson, "1.1.0"));
        Assert.Null(UpdateService.ParseReleaseJson(sameJson, "1.2.0"));
    }

    [Fact]
    public void IsStandardInstalledPath_DetectsCorrectly()
    {
        string standardDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OW Helper");
        string standardExe = Path.Combine(standardDir, "OwHelper.Desktop.exe");
        string portableExe = @"D:\Tools\OwHelper\OwHelper.Desktop.exe";

        Assert.True(UpdateService.IsStandardInstalledPath(standardExe));
        Assert.False(UpdateService.IsStandardInstalledPath(portableExe));
        Assert.False(UpdateService.IsStandardInstalledPath(null));
        Assert.False(UpdateService.IsStandardInstalledPath(""));
    }

    [Fact]
    public void CreateRestartScript_GeneratesValidCmdFile()
    {
        string installerPath = @"C:\Temp\Setup.exe";
        string scriptPath = UpdateService.CreateRestartScript(installerPath);

        Assert.True(File.Exists(scriptPath));
        string content = File.ReadAllText(scriptPath);
        Assert.Contains("Setup.exe", content);
        Assert.Contains("/SILENT", content);
        Assert.Contains("/SUPPRESSMSGBOXES", content);
        Assert.Contains("OwHelper.Desktop.exe", content);

        File.Delete(scriptPath);
    }

    [Fact]
    public void AppConfig_UpdateSection_DefaultAndRoundtrip()
    {
        var config = new AppConfig();
        Assert.NotNull(config.Update);
        Assert.True(config.Update.AutoCheckOnStartup);

        config.Update.AutoCheckOnStartup = false;
        var clone = config.Clone();
        Assert.False(clone.Update.AutoCheckOnStartup);
    }
}
