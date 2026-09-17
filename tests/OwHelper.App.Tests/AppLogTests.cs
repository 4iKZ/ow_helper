using System;
using System.IO;
using OwHelper;
using Xunit;

namespace OwHelper.App.Tests;

public class AppLogTests
{
    static string TempLogPath()
        => Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"), "test.log");

    [Fact]
    public void Write_ThenTail_ReturnsRecentLines()
    {
        string path = TempLogPath();
        var log = new AppLog(path);
        for (int i = 0; i < 5; i++)
        {
            log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "PULSE_OK", PulseIndex: i));
        }

        var lines = log.Tail(3);

        Assert.Equal(3, lines.Count);
        Assert.Contains("PULSE_OK", lines[2]);
        Assert.Contains("pulse=4", lines[2]);
        Assert.Equal(0, log.WriteFailures);
    }

    [Fact]
    public void Write_IncludesStructuredFields()
    {
        string path = TempLogPath();
        var log = new AppLog(path);
        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Warning,
            "PULSE_PARTIAL_FAILURE",
            Pid: 1234,
            Hwnd: (nint)0x5A81C,
            PulseIndex: 7,
            NativeError: 5,
            Operation: "KEYDOWN(0x10)",
            ElapsedMs: 210,
            Message: "test"));

        string line = log.Tail(1)[0];

        Assert.Contains("|WARNING|PULSE_PARTIAL_FAILURE", line);
        Assert.Contains("|pid=1234", line);
        Assert.Contains("|hwnd=0x0005A81C", line);
        Assert.Contains("|pulse=7", line);
        Assert.Contains("|nativeError=5", line);
        Assert.Contains("|op=KEYDOWN(0x10)", line);
        Assert.Contains("|elapsedMs=210", line);
        Assert.Contains("|msg=test", line);
    }

    [Fact]
    public void Tail_WhenFileMissing_ReturnsEmpty()
    {
        var log = new AppLog(TempLogPath());
        Assert.Empty(log.Tail(10));
    }

    [Fact]
    public void Write_WhenPathInvalid_DoesNotThrow()
    {
        var log = new AppLog(Path.Combine(Path.GetTempPath(), "\0invalid", "x.log"));
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_START"));

        Assert.True(log.WriteFailures >= 1);
    }
}
