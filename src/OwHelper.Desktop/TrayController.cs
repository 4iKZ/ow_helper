using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper.Desktop;

public sealed class TrayController
{
    readonly Session session;
    readonly AppLog log;

    public TrayController(Session session, AppLog log, AppConfig config, IReadOnlyList<string> startupProblems)
    {
        this.session = session;
        this.log = log;
        Config = config;
        StartupProblems = startupProblems;
        GpuGuide = BuildGpuGuide(config, GpuEnvironment.Detect());
        UpdateService = new UpdateService(log: log);
    }

    public Session Session => session;

    public AppConfig Config { get; private set; }

    public IReadOnlyList<string> StartupProblems { get; }

    public string? GpuGuide { get; }

    public UpdateService UpdateService { get; }

    internal static string? BuildGpuGuide(AppConfig config, IReadOnlyList<GpuInfo> gpus)
    {
        if (!config.GpuGuideEnabled) return null;
        if (!GpuEnvironment.HasNvidia(gpus)) return null;
        return $"GPU：建议在 NVIDIA 控制面板把「后台应用程序最大帧率」设为 {config.Resource.GpuBackgroundFpsTarget} FPS（需手动设置一次，本程序不会自动改驱动）";
    }

    public async Task<string> ApplySettingsAsync(AppConfig updated)
    {
        updated.Save(AppConfig.DefaultPath);
        Config = updated;
        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Information,
            "CONFIG_SAVE",
            Message: $"keys={string.Join(",", updated.Input.Keys)}; interval={updated.Input.IntervalSeconds}"));

        ResourceApplyResult? applied = await session.ApplyConfigAsync(updated);
        if (applied == null) return "设置已保存并立即生效。";

        string policy = $"{applied.Priority.Name}={(applied.Priority.Success ? "ok" : applied.Priority.Message)}; {applied.Power.Name}={(applied.Power.Success ? "ok" : applied.Power.Message)}";
        return $"设置已保存并立即生效；资源策略已重新应用（{policy}）。";
    }

    public TrayStatus Snapshot()
    {
        GameWindow? target = session.Target;
        return new TrayStatus(
            session.State,
            target != null && target.IsAlive,
            target?.Minimized ?? false,
            target == null ? null : (int)target.Pid,
            target?.Width ?? 0,
            target?.Height ?? 0,
            session.IntervalSec,
            session.PulseCount,
            session.LastPulseAt,
            session.LastResourceApplyPartial,
            session.RunPulseCount,
            session.RunStartedAt);
    }

    public async Task AttachAsync(bool quiet = false)
    {
        try { await session.AttachAsync(quiet); }
        catch (Exception ex) { Fail("attach", ex); }
    }

    public async Task StartAsync()
    {
        try { await session.StartAsync(); }
        catch (Exception ex) { Fail("start", ex); }
    }

    public async Task StopAsync()
    {
        try { await session.StopAsync(); }
        catch (Exception ex) { Fail("stop", ex); }
    }

    public async Task ToggleOffscreenAsync()
    {
        try { await session.ToggleOffscreenAsync(); }
        catch (Exception ex) { Fail("offscreen", ex); }
    }

    public async Task ReattachAsync()
    {
        try { await session.ReattachAsync(); }
        catch (Exception ex) { Fail("reattach", ex); }
    }

    public void OpenLogFolder() => OpenPath(Path.GetDirectoryName(log.FilePath));

    public void OpenConfigFile() => OpenPath(AppConfig.DefaultPath);

    public async Task ShutdownAsync()
    {
        try { await session.CleanupAsync(); }
        catch (Exception ex) { Fail("shutdown", ex); }
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_EXIT", Message: "tray"));
    }

    void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Fail("open:" + path, ex);
        }
    }

    void Fail(string operation, Exception ex)
    {
        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Warning,
            "NATIVE_ERROR",
            Operation: operation,
            Message: ex.Message));
    }
}




