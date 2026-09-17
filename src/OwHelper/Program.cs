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

        var keys = new List<int> { 0x10 };
        string keysDisplay = "shift";
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
        int intervalSec = 30;
        if (args.Length > 1 && int.TryParse(args[1], out int iv) && iv >= 2) intervalSec = iv;

        var session = new Session(
            new PulseRecipe { Keys = keys },
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            Console.WriteLine)
        {
            IntervalSec = intervalSec,
        };

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            session.CleanupAsync().GetAwaiter().GetResult();
            Environment.Exit(0);
        };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => session.CleanupAsync().GetAwaiter().GetResult();

        await new ConsoleUi(session, keysDisplay).RunAsync();
        return 0;
    }
}
