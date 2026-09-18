using System.Diagnostics;
using System.Threading;
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
            restoreStyle: (_, _, _) => new WindowStyleResult(false, "refused", 5, 0),
            ensureShown: (_, _) => true);

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
            restoreStyle: (_, _, _) => new WindowStyleResult(true, "ok", null, 0),
            ensureShown: (_, _) => true);

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
            restoreStyle: (_, _, _) => new WindowStyleResult(false, "refused", 5, 0),
            ensureShown: (_, _) => true);

        var move = placement.MoveOffscreen(hideFromTaskbar: true);

        Assert.False(move.Success);
        Assert.True(placement.StyleRestorePending);
        Assert.True(placement.NeedsRestore);
        Assert.False(placement.IsOffscreen);
    }

    [Fact]
    public void Restore_ForwardsActivateFlag_ToEnsureShown()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        bool? seenActivate = null;
        var placement = new WindowPlacement(
            window.Handle, self.Id,
            moveTo: (_, _, _, _) => new WindowPlacementResult(true, "moved", null),
            restoreStyle: (_, _, _) => new WindowStyleResult(true, "ok", null, 0),
            ensureShown: (_, activate) => { seenActivate = activate; return true; });

        Assert.True(placement.MoveOffscreen().Success);
        WindowProbe.Minimize(window.Handle);
        Assert.True(WindowProbe.IsMinimized(window.Handle));

        _ = placement.Restore(activate: false);
        Assert.Equal(false, seenActivate);

        Assert.True(placement.MoveOffscreen().Success);
        WindowProbe.Minimize(window.Handle);

        _ = placement.Restore(activate: true);
        Assert.Equal(true, seenActivate);
    }

    [Fact]
    public void Restore_NoActivate_DoesNotStealForeground()
    {
        using var background = new FakeWindow(topLevel: true);
        using var target = new FakeWindow(topLevel: true);
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(target.Handle, self.Id);
        Assert.True(placement.MoveOffscreen(hideFromTaskbar: false).Success);
        WindowProbe.Minimize(target.Handle);
        Assert.True(WindowProbe.IsMinimized(target.Handle));

        if (!InteractiveForegroundAvailable(background.Handle)) return;

        var restore = placement.Restore(activate: false);

        Assert.True(restore.Success, restore.Message);
        Assert.False(WindowProbe.IsMinimized(target.Handle));
        Assert.Equal(background.Handle, WindowProbe.ForegroundWindow());
    }

    [Fact]
    public void Restore_Activate_BringsWindowToForeground()
    {
        using var background = new FakeWindow(topLevel: true);
        using var target = new FakeWindow(topLevel: true);
        using var self = Process.GetCurrentProcess();
        var placement = new WindowPlacement(target.Handle, self.Id);
        Assert.True(placement.MoveOffscreen(hideFromTaskbar: false).Success);
        WindowProbe.Minimize(target.Handle);
        if (!InteractiveForegroundAvailable(background.Handle)) return;

        var restore = placement.Restore(activate: true);

        Assert.True(restore.Success, restore.Message);
        Assert.Equal(target.Handle, WindowProbe.ForegroundWindow());
        WindowProbe.TrySetForeground(background.Handle);
    }

    static bool InteractiveForegroundAvailable(IntPtr background)
    {
        // 无交互桌面的会话（如 CI 服务会话）既拿不到前台权限，也恢复不了最小化窗口：跳过前台断言。
        if (!CanRestoreMinimized()) return false;
        return WindowProbe.TrySetForeground(background);
    }

    static bool CanRestoreMinimized()
    {
        using var scratch = new FakeWindow(topLevel: true);
        WindowProbe.Minimize(scratch.Handle);
        if (!WindowProbe.IsMinimized(scratch.Handle)) return false;
        WindowStyle.EnsureShown(scratch.Handle, activate: true);
        Thread.Sleep(200);
        return !WindowProbe.IsMinimized(scratch.Handle);
    }
}

