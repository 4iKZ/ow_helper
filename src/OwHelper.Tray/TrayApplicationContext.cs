using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using OwHelper.Core;

namespace OwHelper.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    readonly AppLog log;
    readonly TrayController controller;
    readonly NotifyIcon notifyIcon;
    readonly ContextMenuStrip menu;
    readonly Timer timer;
    readonly ToolStripMenuItem statusItem;
    readonly ToolStripMenuItem toggleItem;
    readonly ToolStripMenuItem offscreenItem;
    StatusForm? statusForm;
    Color lastIconColor = Color.Empty;

    public TrayApplicationContext()
    {
        log = new AppLog(AppLog.DefaultFilePath());
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_START", Message: "tray"));

        List<string> problems;
        AppConfig config = AppConfig.Load(AppConfig.DefaultPath, out problems);
        foreach (string problem in problems)
        {
            log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "CONFIG_LOAD_FAILED", Message: problem));
        }
        AppLog.DeleteOlderThan(Path.GetDirectoryName(log.FilePath) ?? "", config.Logging.RetainDays);

        var stateStore = new RuntimeStateStore(RuntimeStateStore.DefaultPath);
        RuntimeRecovery.TryRecover(stateStore, WriteRecoveryNote, ConfirmRecovery);

        var session = new Session(
            config.BuildRecipe(),
            new GameWindowLocator(),
            new PulseSender(),
            process => new ResourceGovernor(process),
            new WindowPlacementController(),
            _ => { },
            log,
            stateStore)
        {
            IntervalSec = config.Input.IntervalSeconds,
            JitterPercent = config.Input.JitterPercent,
            TargetProcessName = config.Target.ProcessName,
            Policy = config.BuildPolicy(),
            SkipWhenForeground = config.Input.SkipWhenTargetForeground,
            AllowMoveOffscreen = config.Window.AllowMoveOffscreen,
            KeepOffscreenAcrossRestart = config.Window.KeepOffscreenAcrossRestart,
        };

        controller = new TrayController(session, log, config);

        statusItem = new ToolStripMenuItem("…") { Enabled = false };
        toggleItem = new ToolStripMenuItem("开始", null, async (s, e) =>
        {
            if (controller.Session.IsRunning) await controller.StopAsync();
            else await controller.StartAsync();
            Refresh();
        });
        offscreenItem = new ToolStripMenuItem("移出屏幕", null, async (s, e) =>
        {
            await controller.ToggleOffscreenAsync();
            Refresh();
        });
        var reattachItem = new ToolStripMenuItem("重新检测 / 重挂", null, async (s, e) =>
        {
            await controller.ReattachAsync();
            Refresh();
        });
        var statusWindowItem = new ToolStripMenuItem("打开状态窗口", null, (s, e) => ShowStatusWindow());
        var logsItem = new ToolStripMenuItem("打开日志文件夹", null, (s, e) => controller.OpenLogFolder());
        var configItem = new ToolStripMenuItem("打开配置文件", null, (s, e) => controller.OpenConfigFile());
        var exitItem = new ToolStripMenuItem("退出（恢复资源与窗口）", null, async (s, e) => await ExitAsync());

        menu = new ContextMenuStrip { Renderer = new PaperMenuRenderer(), BackColor = Palette.Panel };
        menu.Items.AddRange(new ToolStripItem[]
        {
            statusItem,
            new ToolStripSeparator(),
            toggleItem,
            offscreenItem,
            reattachItem,
            statusWindowItem,
            new ToolStripSeparator(),
            logsItem,
            configItem,
            new ToolStripSeparator(),
            exitItem,
        });

        notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(Palette.StatusStopped),
            Text = "OW Helper",
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += (s, e) => ShowStatusWindow();

        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => Refresh();
        timer.Start();

        _ = controller.AttachAsync();
        Refresh();
    }

    void Refresh()
    {
        TrayStatus status = controller.Snapshot();
        string headline = TrayStatusMapper.Headline(status);
        statusItem.Text = headline;
        notifyIcon.Text = TrimToTrayLimit($"OW Helper — {headline}");
        toggleItem.Text = controller.Session.IsRunning ? "停止" : "开始";
        offscreenItem.Text = controller.Session.IsOffscreen ? "还原窗口" : "移出屏幕";

        Color color = TrayStatusMapper.StatusColor(status);
        if (color != lastIconColor)
        {
            Icon? previous = notifyIcon.Icon;
            notifyIcon.Icon = TrayIconFactory.Create(color);
            previous?.Dispose();
            lastIconColor = color;
        }

        statusForm?.RefreshStatus(status);
        ShowPendingNotices();
    }

    void ShowPendingNotices()
    {
        var pending = new List<string>();
        string? notice;
        while ((notice = controller.Session.DequeueNotice()) != null)
        {
            pending.Add(notice);
        }
        if (pending.Count == 0) return;

        string text = string.Join(Environment.NewLine, pending);
        if (text.Length > 250) text = text[..249] + "…";
        notifyIcon.BalloonTipTitle = "OW Helper";
        notifyIcon.BalloonTipText = text;
        notifyIcon.ShowBalloonTip(5000);
    }

    void ShowStatusWindow()
    {
        statusForm ??= new StatusForm(controller);
        statusForm.RefreshStatus(controller.Snapshot());
        statusForm.Show();
        statusForm.BringToFront();
    }

    async Task ExitAsync()
    {
        timer.Stop();
        notifyIcon.Visible = false;
        statusForm?.CloseForExit();
        await controller.ShutdownAsync();
        notifyIcon.Dispose();
        ExitThread();
    }

    void WriteRecoveryNote(string message)
    {
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "CONFIG_LOAD_FAILED", Operation: "recovery", Message: message));
    }

    static bool ConfirmRecovery(string prompt)
        => MessageBox.Show(prompt, "OW Helper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    static string TrimToTrayLimit(string text) => text.Length <= 63 ? text : text[..62] + "…";
}
