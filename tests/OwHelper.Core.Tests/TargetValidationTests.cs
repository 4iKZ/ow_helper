using System;
using System.Diagnostics;
using Xunit;

namespace OwHelper.Core.Tests;

public class TargetValidationTests
{
    [Fact]
    public void Validate_LiveWindowWithMatchingPid_IsValid()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();

        var result = TargetValidation.Validate(window.Handle, self.Id, self, null);

        Assert.True(result.Valid);
    }

    [Fact]
    public void Validate_PidMismatch_IsInvalid()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();

        var result = TargetValidation.Validate(window.Handle, self.Id + 1, self, null);

        Assert.False(result.Valid);
        Assert.Contains("pid", result.Reason);
    }

    [Fact]
    public void Validate_DestroyedWindow_IsInvalid()
    {
        using var self = Process.GetCurrentProcess();
        var window = new FakeWindow();
        var handle = window.Handle;
        window.Dispose();

        var result = TargetValidation.Validate(handle, self.Id, self, null);

        Assert.False(result.Valid);
        Assert.Contains("gone", result.Reason);
    }

    [Fact]
    public void Validate_ExitedProcess_IsInvalid()
    {
        using var window = new FakeWindow();
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true });
        Assert.NotNull(process);
        process.WaitForExit();

        var result = TargetValidation.Validate(window.Handle, process.Id, process, null);

        Assert.False(result.Valid);
        Assert.Contains("exited", result.Reason);
        process.Dispose();
    }

    [Fact]
    public void Validate_StartTimeMismatch_IsInvalid()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();

        var result = TargetValidation.Validate(window.Handle, self.Id, self, DateTime.UtcNow.AddMinutes(-5));

        Assert.False(result.Valid);
        Assert.Contains("restarted", result.Reason);
    }
}
