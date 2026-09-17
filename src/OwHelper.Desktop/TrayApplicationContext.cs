using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using OwHelper.Core;

namespace OwHelper.Desktop;

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
    MainForm? mainForm;
    Color lastIconColor = Color.Empty;
    bool minimizeHintShown;
    int tick;

    public TrayApplicationContext()
    {
        log = new AppLog(AppLog.DefaultFilePath());
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "APP_START", Message: "desktop"));

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
            AppMessages.Write,
            log,
            stateStore);
        session.ApplyConfig(config);

        controller = new TrayController(session, log, config);

        statusItem = new ToolStripMenuItem("…") { Enabled = false };
        var openItem = new ToolStripMenuItem("打开主窗口", null, (s, e) => ShowMainWindow());
        toggleItem = new ToolStripMenuItem("开始挂机", null, async (s, e) =>
        {
            if (controller.Session.IsRunning) await controller.StopAsync();
            else await controller.StartAsync();
            Refresh();
        });
        offscreenItem = new ToolStripMenuItem("老板键", null, async (s, e) =>
        {
            await controller.ToggleOffscreenAsync();
            Refresh();
        });
        var reattachItem = new ToolStripMenuItem("重新连接游戏", null, async (s, e) =>
        {
            await controller.ReattachAsync();
            Refresh();
        });
        var settingsItem = new ToolStripMenuItem("设置…", null, (s, e) => ShowSettings());
        var logsItem = new ToolStripMenuItem("打开运行记录", null, (s, e) => controller.OpenLogFolder());
        var configItem = new ToolStripMenuItem("打开设置文件", null, (s, e) => controller.OpenConfigFile());
        var exitItem = new ToolStripMenuItem("退出（恢复游戏设置）", null, async (s, e) => await ExitAsync());

        menu = new ContextMenuStrip { Renderer = new PaperMenuRenderer(), BackColor = Palette.Panel };
        menu.Items.AddRange(new ToolStripItem[]
        {
            statusItem,
            new ToolStripSeparator(),
            openItem,
            new ToolStripSeparator(),
            toggleItem,
            offscreenItem,
            reattachItem,
            new ToolStripSeparator(),
            settingsItem,
            logsItem,
            configItem,
            new ToolStripSeparator(),
            exitItem,
        });

        notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(Palette.StatusStopped),
            Text = "OW 助手",
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => Refresh();
        timer.Start();

        _ = controller.AttachAsync();
        ShowMainWindow();
        Refresh();
    }

    void Refresh()
    {
        tick++;
        if (tick % 5 == 0 && !controller.Session.IsRunning && controller.Session.Target == null)
        {
            _ = controller.AttachAsync(quiet: true);
        }

        TrayStatus status = controller.Snapshot();
        string trayText = TrayStatusMapper.TrayText(status);
        statusItem.Text = trayText;
        notifyIcon.Text = trayText;
        toggleItem.Text = controller.Session.IsRunning ? "停止挂机" : "开始挂机";
        offscreenItem.Text = controller.Session.IsOffscreen ? "恢复游戏窗口" : "老板键";

        Color color = TrayStatusMapper.StatusColor(status);
        if (color != lastIconColor)
        {
            Icon? previous = notifyIcon.Icon;
            notifyIcon.Icon = TrayIconFactory.Create(color);
            previous?.Dispose();
            lastIconColor = color;
        }

        mainForm?.RefreshStatus();
        ShowPendingNotices();
    }

    void ShowPendingNotices()
    {
        var pending = new List<string>();
        SessionNotice? notice;
        while ((notice = controller.Session.DequeueNotice()) != null)
        {
            pending.Add(PlainLanguage.Notice(notice));
        }
        if (pending.Count == 0) return;

        string text = string.Join(Environment.NewLine, pending);
        if (text.Length > 250) text = text[..249] + "…";
        notifyIcon.BalloonTipTitle = "OW 助手";
        notifyIcon.BalloonTipText = text;
        notifyIcon.ShowBalloonTip(5000);
    }

    void ShowMainWindow()
    {
        if (mainForm == null || mainForm.IsDisposed)
        {
            mainForm = new MainForm(controller);
            mainForm.HiddenToTray += OnHiddenToTray;
            mainForm.BossKeyUsed += OnBossKeyUsed;
            MainForm = mainForm;
        }
        mainForm.Show();
        if (mainForm.WindowState == FormWindowState.Minimized)
        {
            mainForm.WindowState = FormWindowState.Normal;
        }
        mainForm.BringToFront();
        mainForm.RefreshStatus();
    }

    void OnHiddenToTray()
    {
        if (minimizeHintShown) return;
        minimizeHintShown = true;
        notifyIcon.BalloonTipTitle = "OW 助手还在后台运行";
        notifyIcon.BalloonTipText = "挂机不会中断。想停止或打开窗口，双击（或右键）右下角的托盘图标即可。";
        notifyIcon.ShowBalloonTip(6000);
    }

    void OnBossKeyUsed()
    {
        notifyIcon.BalloonTipTitle = "已进入老板键";
        notifyIcon.BalloonTipText = "游戏窗口和本窗口都已隐藏，挂机继续。想恢复时，右键托盘图标 → 恢复游戏窗口。";
        notifyIcon.ShowBalloonTip(5000);
    }

    void ShowSettings()
    {
        using var form = new SettingsForm(controller);
        form.ShowDialog();
        Refresh();
    }

    async Task ExitAsync()
    {
        timer.Stop();
        notifyIcon.Visible = false;
        mainForm?.CloseForExit();
        await controller.ShutdownAsync();
        notifyIcon.Dispose();
        ExitThread();
    }

    void WriteRecoveryNote(string message)
    {
        log.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "CONFIG_LOAD_FAILED", Operation: "recovery", Message: message));
    }

    static bool ConfirmRecovery(string prompt)
        => MessageBox.Show(prompt, "OW 助手", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}


