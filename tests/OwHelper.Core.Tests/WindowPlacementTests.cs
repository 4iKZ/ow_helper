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
    public void BossKey_HidesFromTaskbarThenRestoresEverything()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        long originalExStyle = WindowProbe.GetExStyle(window.Handle);
        var original = window.GetRect();
        var placement = new WindowPlacement(window.Handle, self.Id);

        var move = placement.MoveOffscreen(hideFromTaskbar: true);

        Assert.True(move.Success);
        Assert.True(placement.IsOffscreen);
        Assert.True(placement.StyleRestorePending);
        Assert.Equal(-10000, window.GetRect().Left);
        Assert.True((WindowProbe.GetExStyle(window.Handle) & 0x00000080L) != 0);

        var restore = placement.Restore();

        Assert.True(restore.Success);
        Assert.False(placement.StyleRestorePending);
        Assert.False(placement.IsOffscreen);
        Assert.Equal(originalExStyle, WindowProbe.GetExStyle(window.Handle));
        Assert.Equal(original.Left, window.GetRect().Left);
    }

    [Fact]
    public void MoveOffscreen_WithoutBossKey_KeepsTaskbarStyle()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        long originalExStyle = WindowProbe.GetExStyle(window.Handle);
        var placement = new WindowPlacement(window.Handle, self.Id);

        Assert.True(placement.MoveOffscreen(hideFromTaskbar: false).Success);

        Assert.False(placement.StyleRestorePending);
        Assert.Equal(originalExStyle, WindowProbe.GetExStyle(window.Handle));
        Assert.Equal(-10000, window.GetRect().Left);
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

    [Fact]
    public void Restore_StyleFailure_KeepsStylePending()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(
            window.Handle, self.Id,
            moveTo: (_, _, _, _) => new WindowPlacementResult(true, "moved", null),
            restoreStyle: (_, _, _) => new WindowStyleResult(false, "refused", 5, 0));

        Assert.True(placement.MoveOffscreen(hideFromTaskbar: true).Success);
        var restore = placement.Restore();

        Assert.False(restore.Success);
        Assert.False(placement.PositionRestorePending);
        Assert.True(placement.StyleRestorePending);
        Assert.True(placement.NeedsRestore);
        Assert.False(placement.IsOffscreen);
        Assert.Equal(PlacementState.RestoreFailed, placement.State);
    }

    [Fact]
    public void Restore_PositionFailure_KeepsPositionPending_AndRetries()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        int attempts = 0;
        var placement = new WindowPlacement(
            window.Handle, self.Id,
            moveTo: (_, _, _, _) => ++attempts == 2
                ? new WindowPlacementResult(false, "SetWindowPos failed", 5)
                : new WindowPlacementResult(true, "moved", null),
            restoreStyle: (_, _, _) => new WindowStyleResult(true, "ok", null, 0));

        Assert.True(placement.MoveOffscreen().Success);
        Assert.True(placement.PositionRestorePending);

        var first = placement.Restore();
        Assert.False(first.Success);
        Assert.True(placement.IsOffscreen);
        Assert.True(placement.NeedsRestore);

        var retry = placement.Restore();
        Assert.True(retry.Success);
        Assert.False(placement.NeedsRestore);
        Assert.False(placement.IsOffscreen);
    }

    [Fact]
    public void MoveOffscreen_RollbackFailure_KeepsStylePending()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(
            window.Handle, self.Id,
            moveTo: (_, _, _, _) => new WindowPlacementResult(false, "SetWindowPos failed", 5),
            restoreStyle: (_, _, _) => new WindowStyleResult(false, "refused", 5, 0));

        var move = placement.MoveOffscreen(hideFromTaskbar: true);

        Assert.False(move.Success);
        Assert.True(placement.StyleRestorePending);
        Assert.True(placement.NeedsRestore);
        Assert.False(placement.IsOffscreen);
    }
}

