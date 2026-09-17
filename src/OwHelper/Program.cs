using System;
using System.Collections.Generic;
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

        using var single = new SingleInstance(AppStartup.SingleInstanceName);
        if (!single.Acquired)
        {
            Console.WriteLine("OwHelper 已在运行（单实例限制）。");
            return 2;
        }

        List<int>? cliKeys = null;
        int? cliInterval = null;
        string keysDisplay = "";
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            try
            {
                cliKeys = args[0].Split(',').Select(KeyNames.Parse).ToList();
                keysDisplay = args[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("用法: OwHelper.exe [按键,逗号分隔] [间隔秒]   例: OwHelper.exe shift,w 30");
                return 1;
            }
        }
        if (args.Length > 1 && int.TryParse(args[1], out int iv) && iv >= 2) cliInterval = iv;

        AppStartupResult startup = AppStartup.Initialize(string.Join(" ", args), Console.WriteLine, prompt =>
        {
            Console.Write(prompt + " ");
            ConsoleKeyInfo key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);
            return key.Key == ConsoleKey.Y;
        });
        foreach (string problem in startup.Problems)
        {
            Console.WriteLine("配置: " + problem);
        }
        Console.WriteLine("=== OW 后台挂机助手 ===");

        if (keysDisplay.Length == 0) keysDisplay = string.Join(",", startup.Config.Input.Keys);

        Session session = startup.Session;
        if (cliKeys != null)
        {
            PulseRecipe baseRecipe = startup.Config.BuildRecipe();
            session.UpdateRecipe(new PulseRecipe
            {
                Keys = cliKeys,
                SendFocus = baseRecipe.SendFocus,
                SendActivate = baseRecipe.SendActivate,
                SendActivateApp = baseRecipe.SendActivateApp,
                FocusWaitMs = baseRecipe.FocusWaitMs,
                HoldMs = baseRecipe.HoldMs,
            });
        }
        if (cliInterval.HasValue) session.IntervalSec = cliInterval.Value;

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            session.CleanupAsync().GetAwaiter().GetResult();
            startup.Log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_EXIT", Message: "ctrl+c"));
            Environment.Exit(0);
        };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => session.CleanupAsync().GetAwaiter().GetResult();

        await new ConsoleUi(session, keysDisplay, startup.Log, startup.Config.Resource.GpuBackgroundFpsTarget, startup.Config.GpuGuideEnabled).RunAsync();
        startup.Log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_EXIT", Message: "quit"));
        return 0;
    }
}
