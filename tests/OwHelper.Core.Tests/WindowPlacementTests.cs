using System.Diagnostics;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.Core.Tests;

public class WindowPlacementTests
{
    [Fact]
    public void MoveOffscreen_AndRestore_RoundTrips()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var original = window.GetRect();
        var placement = new WindowPlacement(window.Handle, self.Id);

        var move = placement.MoveOffscreen();
        Assert.True(move.Success);
        Assert.True(placement.IsOffscreen);
        Assert.Equal(-10000, window.GetRect().Left);

        var restore = placement.Restore();
        Assert.True(restore.Success);
        Assert.False(placement.IsOffscreen);
        Assert.Equal(original.Left, window.GetRect().Left);
        Assert.Equal(original.Top, window.GetRect().Top);
    }

    [Fact]
    public void MoveOffscreen_WithWrongPid_IsRefused()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(window.Handle, self.Id + 1);

        var move = placement.MoveOffscreen();

        Assert.False(move.Success);
        Assert.False(placement.IsOffscreen);
        Assert.NotEqual(-10000, window.GetRect().Left);
    }

    [Fact]
    public void MoveOffscreen_OnDestroyedWindow_FailsWithoutChangingState()
    {
        var window = new FakeWindow();
        var handle = window.Handle;
        window.Dispose();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(handle, self.Id);

        var move = placement.MoveOffscreen();

        Assert.False(move.Success);
        Assert.False(placement.IsOffscreen);
        Assert.Equal(PlacementState.None, placement.State);
    }

    [Fact]
    public void Restore_AfterTargetLost_MarksStale()
    {
        var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(window.Handle, self.Id);
        Assert.True(placement.MoveOffscreen().Success);
        window.Dispose();

        var restore = placement.Restore();

        Assert.False(restore.Success);
        Assert.Equal(PlacementState.Stale, placement.State);
    }

    [Fact]
    public void Restore_WithoutAnyMove_IsNoOp()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(window.Handle, self.Id);

        var restore = placement.Restore();

        Assert.True(restore.Success);
        Assert.Equal(PlacementState.None, placement.State);
    }
}

