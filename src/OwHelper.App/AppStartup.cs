using System;
using System.Collections.Generic;
using System.IO;
using OwHelper.Core;

namespace OwHelper;

public sealed record AppStartupResult(
    AppLog Log,
    AppConfig Config,
    Session Session,
    IReadOnlyList<string> Problems);

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

        return new AppStartupResult(log, config, session, problems);
    }
}
