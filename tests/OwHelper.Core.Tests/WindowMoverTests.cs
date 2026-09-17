using System.Diagnostics;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.Core.Tests;

public class WindowMoverTests
{
    [Fact]
    public void MoveTo_MovesWindowAndVerifies()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();

        var result = WindowMover.MoveTo(window.Handle, self.Id, 123, 456);

        Assert.True(result.Success);
        var rect = window.GetRect();
        Assert.Equal(123, rect.Left);
        Assert.Equal(456, rect.Top);
    }

    [Fact]
    public void MoveTo_WithWrongPid_IsRefused()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();

        var result = WindowMover.MoveTo(window.Handle, self.Id + 1, 1, 1);

        Assert.False(result.Success);
        Assert.Contains("pid", result.Message);
    }

    [Fact]
    public void MoveTo_OnDestroyedWindow_Fails()
    {
        var window = new FakeWindow();
        var handle = window.Handle;
        window.Dispose();

        var result = WindowMover.MoveTo(handle, 1234, 0, 0);

        Assert.False(result.Success);
        Assert.Contains("gone", result.Message);
    }
}
