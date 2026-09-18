using System;
using System.Collections.Generic;
using System.IO;
using OwHelper.Core;

namespace OwHelper;

public sealed record AppStartupResult(
    AppLog Log,
    AppConfig Config,
    Session Session,
    IReadOnlyList<string> Problems,
    string? UpdateFailureMessage = null);

public static class AppStartup
{
    public const string SingleInstanceName = @"Local\OwHelper.SingleInstance";

    public static AppStartupResult Initialize(string frontendTag, Action<string> output, Func<string, bool> confirmRecovery)
    {
        List<string> problems;
        AppConfig config = AppConfig.Load(AppConfig.DefaultPath, out problems);

        var log = new AppLog(AppLog.DefaultFilePath(), config.MinLogLevel());
        foreach (string problem in problems)
        {
            log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "CONFIG_LOAD_WARNING", Message: problem));
        }
        AppLog.DeleteOlderThan(Path.GetDirectoryName(log.FilePath) ?? "", config.Logging.RetainDays);
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_START", Message: frontendTag));

        // 清理超过 24 小时的更新临时文件
        UpdateService.CleanUpdateTempFiles();

        // 读取并处理上次更新执行结果
        string? updateFailureMessage = null;
        UpdateResultRecord? updateResult = UpdateResultStore.Load();
        if (updateResult != null)
        {
            if (updateResult.InstallerExitCode == 0)
            {
                log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "UPDATE_INSTALL_SUCCESS", Message: $"更新安装成功 (v{updateResult.Version})"));
            }
            else
            {
                updateFailureMessage = $"上次自动更新未成功完成（安装程序退出代码: {updateResult.InstallerExitCode}）。";
                log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_INSTALL_FAILED", Message: updateFailureMessage));
                problems.Add(updateFailureMessage);
            }
            UpdateResultStore.Delete();
        }

        var stateStore = new RuntimeStateStore(RuntimeStateStore.DefaultPath);
        var recoveryMessages = new List<string>();
        RecoveryOutcome recovery = RuntimeRecovery.TryRecover(stateStore, recoveryMessages.Add, confirmRecovery);
        if (recovery != RecoveryOutcome.None)
        {
            foreach (string message in recoveryMessages)
            {
                log.Write(new LogEntry(
                    DateTimeOffset.Now,
                    recovery == RecoveryOutcome.Success ? LogLevel.Information : LogLevel.Warning,
                    recovery.ToEventName(),
                    Message: message));
            }
        }

        var session = new Session(
            config.BuildRecipe(),
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            output,
            log,
            stateStore);
        session.ApplyConfig(config);

        return new AppStartupResult(log, config, session, problems, updateFailureMessage);
    }
}
