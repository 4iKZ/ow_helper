using System;
using System.IO;
using Xunit;

namespace OwHelper.App.Tests;

public class UpdateResultStoreTests : IDisposable
{
    readonly string testDir;
    readonly string testFile;

    public UpdateResultStoreTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "OwHelper_UpdateResultTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        testFile = Path.Combine(testDir, "update-result.json");
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
    public void SaveAndLoad_SuccessResult_RoundtripsCorrectly()
    {
        var record = new UpdateResultRecord(
            SchemaVersion: 1,
            Version: "1.2.0",
            CompletedAt: DateTimeOffset.UtcNow,
            InstallerExitCode: 0,
            Restarted: true);

        UpdateResultStore.Save(record, testFile);

        Assert.True(File.Exists(testFile));
        UpdateResultRecord? loaded = UpdateResultStore.Load(testFile);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("1.2.0", loaded.Version);
        Assert.Equal(0, loaded.InstallerExitCode);
        Assert.True(loaded.Restarted);
    }

    [Fact]
    public void SaveAndLoad_FailureResult_RoundtripsCorrectly()
    {
        var record = new UpdateResultRecord(
            SchemaVersion: 1,
            Version: "1.2.0",
            CompletedAt: DateTimeOffset.UtcNow,
            InstallerExitCode: 4,
            Restarted: false);

        UpdateResultStore.Save(record, testFile);

        UpdateResultRecord? loaded = UpdateResultStore.Load(testFile);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("1.2.0", loaded.Version);
        Assert.Equal(4, loaded.InstallerExitCode);
        Assert.False(loaded.Restarted);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsNull()
    {
        string nonExistent = Path.Combine(testDir, "non-existent.json");
        UpdateResultRecord? loaded = UpdateResultStore.Load(nonExistent);
        Assert.Null(loaded);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsNullWithoutThrowing()
    {
        File.WriteAllText(testFile, "{ corrupted json invalid syntax ... ");
        UpdateResultRecord? loaded = UpdateResultStore.Load(testFile);
        Assert.Null(loaded);
    }

    [Fact]
    public void Delete_RemovesFileAndDoesNotThrowWhenAbsent()
    {
        File.WriteAllText(testFile, "{}");
        Assert.True(File.Exists(testFile));

        UpdateResultStore.Delete(testFile);
        Assert.False(File.Exists(testFile));

        // Calling delete again on non-existent file does not throw
        UpdateResultStore.Delete(testFile);
    }
}
