using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OwHelper;
using OwHelper.Core;
using OwHelper.Desktop;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.App.Tests;

public class TrayControllerTests
{
    sealed class StubPlacement : IWindowPlacementController
    {
        public bool IsOffscreen => false;
        public bool StyleRestorePending => false;
        public bool NeedsRestore => false;
        public long OriginalExStyle => 0;
        public WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid, bool hideFromTaskbar) => new(true, "ok", null);
        public WindowPlacementResult Restore(bool activate = false) => new(true, "ok", null);
        public bool TryGetOriginalPosition(out int left, out int top) { left = 0; top = 0; return false; }
    }

    static TrayController Build(AppConfig? config = null, IReadOnlyList<string>? problems = null)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "OwHelperTests", Guid.NewGuid().ToString("N"), "test.log");
        var session = new Session(
            new PulseRecipe { Keys = new[] { 0x10 } },
            null!,
            null!,
            _ => null!,
            new StubPlacement(),
            _ => { });
        return new TrayController(session, new AppLog(logPath), config ?? new AppConfig(), problems ?? Array.Empty<string>());
    }

    [Fact]
    public void ExposesStartupProblems()
    {
        var controller = Build(problems: new[] { "input.intervalSeconds=500 超出 5-300，已调整为 300" });

        Assert.Single(controller.StartupProblems);
    }

    [Fact]
    public void BuildGpuGuide_NvidiaDetected_ReturnsGuidanceWithoutClaimingEnforced()
    {
        var config = new AppConfig();
        var gpus = new[] { new GpuInfo("NVIDIA", "NVIDIA GeForce RTX 4060") };

        string? guide = TrayController.BuildGpuGuide(config, gpus);

        Assert.NotNull(guide);
        Assert.Contains("20", guide);
        Assert.DoesNotContain("已限制", guide);
    }

    [Fact]
    public void BuildGpuGuide_DisabledPolicy_ReturnsNull()
    {
        var config = new AppConfig();
        config.Resource.GpuPolicyMode = "Disabled";
        var gpus = new[] { new GpuInfo("NVIDIA", "NVIDIA GeForce RTX 4060") };

        Assert.Null(TrayController.BuildGpuGuide(config, gpus));
    }

    [Fact]
    public void BuildGpuGuide_NoNvidia_ReturnsNull()
    {
        var config = new AppConfig();
        Assert.Null(TrayController.BuildGpuGuide(config, Array.Empty<GpuInfo>()));
    }

    [Fact]
    public async Task PrepareForApplicationExitAsync_IdleSession_ReturnsSafeToExit()
    {
        var controller = Build();
        ApplicationCleanupResult result = await controller.PrepareForApplicationExitAsync();

        Assert.True(result.SafeToExit);
        Assert.True(result.SessionStopped);
        Assert.True(result.WindowFullyRestored);
        Assert.True(result.ResourceRestoreSucceeded);
    }
}
