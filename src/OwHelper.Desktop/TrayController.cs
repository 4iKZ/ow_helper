using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OwHelper.Core;

namespace OwHelper.Desktop;

public sealed class TrayController
{
    readonly Session session;
    readonly AppLog log;
    readonly AppConfig config;
    IReadOnlyList<GpuInfo>? gpus;

    public TrayController(Session session, AppLog log, AppConfig config)
    {
        this.session = session;
        this.log = log;
        this.config = config;
    }

    public Session Session => session;

    public AppConfig Config => config;

    public async Task<string> ApplySettingsAsync(AppConfig updated)
    {
        session.IntervalSec = updated.Input.IntervalSeconds;
        session.JitterPercent = updated.Input.JitterPercent;
        session.Policy = updated.BuildPolicy();
        session.SkipWhenForeground = updated.Input.SkipWhenTargetForeground;
        session.AllowMoveOffscreen = updated.Window.AllowMoveOffscreen;
        session.KeepOffscreenAcrossRestart = updated.Window.KeepOffscreenAcrossRestart;
        session.UpdateRecipe(updated.BuildRecipe());
        updated.Save(AppConfig.DefaultPath);
        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Information,
            "CONFIG_APPLIED",
            Message: $"keys={string.Join(",", updated.Input.Keys)}; interval={updated.Input.IntervalSeconds}"));

        if (!session.IsRunning) return "设置已保存。按键与时序将在下次「开始」生效。";

        ResourceApplyResult? applied = await session.ReapplyPolicyAsync();
        if (applied == null) return "设置已保存。按键与时序将在下次「开始」生效。";

        string policy = $"{applied.Priority.Name}={(applied.Priority.Success ? "ok" : applied.Priority.Message)}; {applied.Power.Name}={(applied.Power.Success ? "ok" : applied.Power.Message)}";
        return $"设置已保存；资源策略已重新应用（{policy}）。按键与时序将在下次「开始」生效。";
    }

    public TrayStatus Snapshot()
    {
        GameWindow? target = session.Target;
        gpus ??= GpuEnvironment.Detect();
        return new TrayStatus(
            session.State,
            target != null && target.IsAlive,
            target == null ? null : (int)target.Pid,
            target?.Handle ?? nint.Zero,
            target?.Width ?? 0,
            target?.Height ?? 0,
            session.IntervalSec,
            session.JitterPercent,
            session.PulseCount,
            session.LastPulseAt,
            log.FilePath,
            AppConfig.DefaultPath,
            string.Join("; ", gpus.Select(g => g.Name)),
            BuildGpuGuidance(gpus),
            session.LastResourceApplyPartial);
    }

    string BuildGpuGuidance(IReadOnlyList<GpuInfo> detected)
    {
        if (!config.GpuGuideEnabled || !GpuEnvironment.HasNvidia(detected)) return "";
        return $"NVIDIA 控制面板 → Overwatch.exe 后台最大帧率 = {config.Resource.GpuBackgroundFpsTarget} FPS";
    }

    public async Task AttachAsync()
    {
        try { await session.AttachAsync(); }
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

