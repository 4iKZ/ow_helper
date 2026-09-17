using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OwHelper;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.App.Tests;

public class RuntimeStateTests
{
    static string TempStatePath()
        => Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"), "runtime-state.json");

    static RuntimeState MakeState(FakeWindow window, Process self, int left, int top)
        => new RuntimeState(1, self.Id, self.StartTime.ToUniversalTime(), window.Handle.ToInt64(), left, top, true, false, 0);

    [Fact]
    public void Store_SaveLoadClear_RoundTrips()
    {
        string path = TempStatePath();
        var store = new RuntimeStateStore(path);
        Assert.Null(store.Load());

        store.Save(new RuntimeState(1, 42, DateTime.UtcNow, 0x5A81CL, 100, 200, true, false, 0));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.Pid);
        Assert.Equal(100, loaded.Left);
        Assert.Equal(200, loaded.Top);

        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void Store_CorruptFile_ReturnsNull()
    {
        string path = TempStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var store = new RuntimeStateStore(path);

        Assert.Null(store.Load());
    }

    [Fact]
    public void TryRecover_WithValidStateAndConfirm_RestoresAndClears()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var store = new RuntimeStateStore(TempStatePath());
        WindowMover.MoveTo(window.Handle, self.Id, -10000, -10000);
        store.Save(MakeState(window, self, 111, 222));
        var logs = new List<string>();

        bool recovered = RuntimeRecovery.TryRecover(store, logs.Add, _ => true);

        Assert.True(recovered);
        Assert.Equal(111, window.GetRect().Left);
        Assert.Null(store.Load());
    }

    [Fact]
    public void TryRecover_WhenDeclined_KeepsStateAndWindow()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var store = new RuntimeStateStore(TempStatePath());
        WindowMover.MoveTo(window.Handle, self.Id, -10000, -10000);
        store.Save(MakeState(window, self, 111, 222));
        var logs = new List<string>();

        bool recovered = RuntimeRecovery.TryRecover(store, logs.Add, _ => false);

        Assert.False(recovered);
        Assert.Equal(-10000, window.GetRect().Left);
        Assert.NotNull(store.Load());
    }

    [Fact]
    public void TryRecover_WithStaleIdentity_ClearsStateWithoutMoving()
    {
        using var window = new FakeWindow();
        using var self = Process.GetCurrentProcess();
        var store = new RuntimeStateStore(TempStatePath());
        WindowMover.MoveTo(window.Handle, self.Id, -10000, -10000);
        store.Save(new RuntimeState(1, self.Id + 1, null, window.Handle.ToInt64(), 111, 222, true, false, 0));
        var logs = new List<string>();

        bool recovered = RuntimeRecovery.TryRecover(store, logs.Add, _ => true);

        Assert.False(recovered);
        Assert.Equal(-10000, window.GetRect().Left);
        Assert.Null(store.Load());
    }

    [Fact]
    public void TryRecover_WithoutStateFile_DoesNothing()
    {
        var store = new RuntimeStateStore(TempStatePath());
        var logs = new List<string>();

        bool recovered = RuntimeRecovery.TryRecover(store, logs.Add, _ => true);

        Assert.False(recovered);
        Assert.Empty(logs);
    }
}


