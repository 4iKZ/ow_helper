using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace OwHelper.App.Tests;

public class UpdateScriptTests : IDisposable
{
    readonly string testDir;

    public UpdateScriptTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "OwHelper_UpdateScriptTests_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData(@"C:\Users\Test User\App & Data\OW Helper", "\"C:\\Users\\Test User\\App & Data\\OW Helper\"")]
    [InlineData(@"C:\A(B)\OW Helper", "\"C:\\A(B)\\OW Helper\"")]
    [InlineData("normal_path", "\"normal_path\"")]
    [InlineData("", "\"\"")]
    [InlineData(null, "\"\"")]
    public void QuoteCmdArgument_HandlesSpecialCharactersSafely(string? input, string expected)
    {
        string quoted = UpdateService.QuoteCmdArgument(input);
        Assert.Equal(expected, quoted);
    }

    [Fact]
    public void QuoteCmdArgument_DoesNotDoubleQuoteAlreadyQuotedInput()
    {
        string input = "\"C:\\Test\\App & Data\"";
        string quoted = UpdateService.QuoteCmdArgument(input);
        Assert.Equal("\"C:\\Test\\App & Data\"", quoted);
    }

    [Fact]
    public void CleanUpdateTempFiles_DeletesOnlyStaleUpdateFiles()
    {
        string tempDir = Path.Combine(testDir, "TempUpdate");
        Directory.CreateDirectory(tempDir);

        string staleDownloading = Path.Combine(tempDir, "setup.exe.downloading");
        string staleSetup = Path.Combine(tempDir, "OwHelper-Setup-1.1.0.exe");
        string staleCmd = Path.Combine(tempDir, "install-12345.cmd");
        string freshSetup = Path.Combine(tempDir, "OwHelper-Setup-1.2.0.exe");
        string unrelatedFile = Path.Combine(tempDir, "important.log");

        File.WriteAllText(staleDownloading, "stale");
        File.WriteAllText(staleSetup, "stale");
        File.WriteAllText(staleCmd, "stale");
        File.WriteAllText(freshSetup, "fresh");
        File.WriteAllText(unrelatedFile, "keep");

        // Backdate stale files by 25 hours
        DateTime past = DateTime.UtcNow.AddHours(-25);
        File.SetLastWriteTimeUtc(staleDownloading, past);
        File.SetLastWriteTimeUtc(staleSetup, past);
        File.SetLastWriteTimeUtc(staleCmd, past);

        int deleted = UpdateService.CleanUpdateTempFiles(tempDir, TimeSpan.FromHours(24));

        Assert.Equal(3, deleted);
        Assert.False(File.Exists(staleDownloading));
        Assert.False(File.Exists(staleSetup));
        Assert.False(File.Exists(staleCmd));
        Assert.True(File.Exists(freshSetup));
        Assert.True(File.Exists(unrelatedFile));
    }

    [Fact]
    public void CreateRestartScript_GeneratesScriptWithGuidAndProperStructure()
    {
        string installerPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string restartExe = Path.Combine(testDir, "OwHelper.Desktop.exe");
        string targetDir = Path.Combine(testDir, "Target (App) & Data");
        string logPath = Path.Combine(testDir, "setup.log");
        string resultPath = Path.Combine(testDir, "update-result.json");

        string scriptPath = UpdateService.CreateRestartScript(
            installerPath: installerPath,
            targetExePath: restartExe,
            targetDirectory: targetDir,
            targetVersion: "1.2.0",
            logPath: logPath,
            updateResultPath: resultPath);

        try
        {
            Assert.True(File.Exists(scriptPath));
            string fileName = Path.GetFileName(scriptPath);
            Assert.StartsWith("install-", fileName);
            Assert.EndsWith(".cmd", fileName);

            string content = File.ReadAllText(scriptPath);
            Assert.Contains("/SILENT", content);
            Assert.Contains("/SUPPRESSMSGBOXES", content);
            Assert.Contains("/NORESTART", content);
            Assert.Contains("/CLOSEAPPLICATIONS", content);
            Assert.Contains("/DIR=", content);
            Assert.Contains("/LOG=", content);
            Assert.Contains("OWH_EXIT", content);
            Assert.Contains("OWH_RESTARTED", content);
            Assert.Contains("schemaVersion", content);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public void CreateRestartScript_WithSpecialCharacters_QuotesAllArgumentsSafely()
    {
        string installerPath = @"C:\Users\Test User\App & Data\Setup.exe";
        string restartExe = @"C:\Users\Test User\App & Data\OW Helper\OwHelper.Desktop.exe";
        string targetDir = @"C:\Users\Test User\App & Data\OW Helper";
        string logPath = @"C:\A(B)\OW Helper\setup.log";
        string resultPath = @"C:\A(B)\OW Helper\update-result.json";

        string scriptPath = UpdateService.CreateRestartScript(
            installerPath: installerPath,
            targetExePath: restartExe,
            targetDirectory: targetDir,
            targetVersion: "1.2.0",
            logPath: logPath,
            updateResultPath: resultPath);

        try
        {
            Assert.True(File.Exists(scriptPath));
            string content = File.ReadAllText(scriptPath);

            Assert.Contains("\"C:\\Users\\Test User\\App & Data\\Setup.exe\"", content);
            Assert.Contains("/DIR=\"C:\\Users\\Test User\\App & Data\\OW Helper\"", content);
            Assert.Contains("/LOG=\"C:\\A(B)\\OW Helper\\setup.log\"", content);
            Assert.Contains("\"C:\\Users\\Test User\\App & Data\\OW Helper\\OwHelper.Desktop.exe\"", content);
            Assert.Contains("\"C:\\A(B)\\OW Helper\\update-result.json\"", content);
            Assert.Contains("OWH_EXIT", content);
            Assert.Contains("OWH_RESTARTED", content);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public void CreateRestartScript_GeneratesValidBatchExitLogic()
    {
        string installerPath = Path.Combine(testDir, "Setup.exe");
        string restartExe = Path.Combine(testDir, "OwHelper.Desktop.exe");

        string scriptPath = UpdateService.CreateRestartScript(installerPath, restartExe);

        try
        {
            string content = File.ReadAllText(scriptPath);

            Assert.Contains("set \"OWH_EXIT=%ERRORLEVEL%\"", content);
            Assert.Contains("set \"OWH_RESTARTED=false\"", content);
            Assert.Contains("if \"%OWH_EXIT%\"==\"0\"", content);
            Assert.Contains("set \"OWH_RESTARTED=true\"", content);
            Assert.Contains("\"installerExitCode\": %OWH_EXIT%", content);
            Assert.Contains("\"restarted\": %OWH_RESTARTED%", content);
            Assert.Contains("exit /b %OWH_EXIT%", content);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }
}
