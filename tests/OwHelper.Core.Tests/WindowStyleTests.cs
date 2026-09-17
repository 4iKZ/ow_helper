using System.Diagnostics;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.Core.Tests;

public class WindowStyleTests
{
    const long WS_EX_TOOLWINDOW = 0x00000080L;
    const long WS_EX_APPWINDOW = 0x00040000L;

    [Fact]
    public void HideFromTaskbar_SetsToolWindowAndClearsAppWindow()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        long before = WindowProbe.GetExStyle(window.Handle);

        var result = WindowStyle.HideFromTaskbar(window.Handle, self.Id);

        Assert.True(result.Success);
        Assert.Equal(before, result.OriginalExStyle);
        long after = WindowProbe.GetExStyle(window.Handle);
        Assert.True((after & WS_EX_TOOLWINDOW) != 0);
        Assert.True((after & WS_EX_APPWINDOW) == 0);
    }

    [Fact]
    public void RestoreStyle_WritesOriginalBack()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        long before = WindowProbe.GetExStyle(window.Handle);
        WindowStyle.HideFromTaskbar(window.Handle, self.Id);

        var result = WindowStyle.RestoreStyle(window.Handle, self.Id, before);

        Assert.True(result.Success);
        Assert.Equal(before, WindowProbe.GetExStyle(window.Handle));
    }

    [Fact]
    public void HideFromTaskbar_WithWrongPid_IsRefused()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        long before = WindowProbe.GetExStyle(window.Handle);

        var result = WindowStyle.HideFromTaskbar(window.Handle, self.Id + 1);

        Assert.False(result.Success);
        Assert.Equal(before, WindowProbe.GetExStyle(window.Handle));
    }

    [Fact]
    public void HideFromTaskbar_OnDestroyedWindow_Fails()
    {
        var window = new FakeWindow();
        var handle = window.Handle;
        window.Dispose();

        var result = WindowStyle.HideFromTaskbar(handle, 1234);

        Assert.False(result.Success);
        Assert.Contains("gone", result.Message);
    }
}
