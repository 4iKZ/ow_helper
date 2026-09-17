using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OwHelper;
using OwHelper.Core;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var single = new SingleInstance(@"Local\OwHelper.SingleInstance");
        if (!single.Acquired)
        {
            Console.WriteLine("OwHelper 已在运行（单实例限制）。");
            return 2;
        }

        List<string> problems;
        AppConfig config = AppConfig.Load(AppConfig.DefaultPath, out problems);

        var log = new AppLog(AppLog.DefaultFilePath(), config.MinLogLevel());
        foreach (string problem in problems)
        {
            Console.WriteLine("配置: " + problem);
            log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "CONFIG_LOAD_FAILED", Message: problem));
        }
        AppLog.DeleteOlderThan(Path.GetDirectoryName(log.FilePath) ?? "", config.Logging.RetainDays);

        List<int> keys = config.BuildRecipe().Keys.ToList();
        string keysDisplay = string.Join(",", config.Input.Keys);
        int intervalSec = config.Input.IntervalSeconds;
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            try
            {
                keys = args[0].Split(',').Select(KeyNames.Parse).ToList();
                keysDisplay = args[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("用法: OwHelper.exe [按键,逗号分隔] [间隔秒]   例: OwHelper.exe shift,w 30");
                return 1;
            }
        }
        if (args.Length > 1 && int.TryParse(args[1], out int iv) && iv >= 2) intervalSec = iv;

        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_START", Message: string.Join(" ", args)));

        Console.WriteLine("=== OW 后台挂机助手 ===");
        var stateStore = new RuntimeStateStore(RuntimeStateStore.DefaultPath);
        RuntimeRecovery.TryRecover(stateStore, Console.WriteLine, prompt =>
        {
            Console.Write(prompt + " ");
            ConsoleKeyInfo key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);
            return key.Key == ConsoleKey.Y;
        });

        var session = new Session(
            new PulseRecipe
            {
                Keys = keys,
                SendFocus = config.Input.SendFocus,
                SendActivate = config.Input.SendActivate,
                SendActivateApp = config.Input.SendActivateApp,
                FocusWaitMs = config.Input.FocusWaitMilliseconds,
                HoldMs = config.Input.HoldMilliseconds,
            },
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            Console.WriteLine,
            log,
            stateStore)
        {
            IntervalSec = intervalSec,
            JitterPercent = config.Input.JitterPercent,
            TargetProcessName = config.Target.ProcessName,
            Policy = config.BuildPolicy(),
            SkipWhenForeground = config.Input.SkipWhenTargetForeground,
            AllowMoveOffscreen = config.Window.AllowMoveOffscreen,
            KeepOffscreenAcrossRestart = config.Window.KeepOffscreenAcrossRestart,
        };

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            session.CleanupAsync().GetAwaiter().GetResult();
            log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_EXIT", Message: "ctrl+c"));
            Environment.Exit(0);
        };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => session.CleanupAsync().GetAwaiter().GetResult();

        await new ConsoleUi(session, keysDisplay, log, config.Resource.GpuBackgroundFpsTarget, config.GpuGuideEnabled).RunAsync();
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_EXIT", Message: "quit"));
        return 0;
    }
}
