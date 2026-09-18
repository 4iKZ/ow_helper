using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        AppStartupResult startup = AppStartup.Initialize("desktop", AppMessages.Write, ConfirmRecovery);
        log = startup.Log;
        AppConfig config = startup.Config;
        controller = new TrayController(startup.Session, log, config, startup.Problems);
        controller.InstallVerifiedPackageAsync = InstallVerifiedUpdateAndExitAsync;

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
        var updateItem = new ToolStripMenuItem("检查更新…", null, async (s, e) =>
        {
            if (mainForm != null && !mainForm.IsDisposed)
            {
                ShowMainWindow();
                await mainForm.TriggerCheckForUpdatesAsync(manual: true);
            }
            else
            {
                string currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                try
                {
                    UpdateCheckResult result = await controller.UpdateService.CheckForUpdatesAsync(currentVer, force: true);
                    if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update != null)
                    {
                        using var dlg = new UpdateDialog(controller, result.Update, currentVer);
                        dlg.ShowDialog();
                    }
                    else if (result.Status == UpdateCheckStatus.UpdateAvailableButManualOnly && result.Update != null)
                    {
                        var choice = MessageBox.Show(result.Message, "检查更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                        if (choice == DialogResult.OK)
                        {
                            try { Process.Start(new ProcessStartInfo(result.Update.HtmlUrl) { UseShellExecute = true }); } catch { }
                        }
                    }
                    else if (result.Status == UpdateCheckStatus.UpToDate)
                    {
                        MessageBox.Show(result.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show(result.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch
                {
                    MessageBox.Show("检查更新失败：无法连接更新服务器。\n当前版本状态未知，请稍后重试。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        });
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
            updateItem,
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
        ShowStartupProblemsOnce();
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

    bool startupProblemsHintShown;

    void ShowStartupProblemsOnce()
    {
        if (startupProblemsHintShown) return;
        startupProblemsHintShown = true;
        if (controller.StartupProblems.Count == 0) return;
        notifyIcon.BalloonTipTitle = "OW 助手";
        notifyIcon.BalloonTipText = $"有 {controller.StartupProblems.Count} 项设置已自动修正，详情见运行记录。";
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


    async Task InstallVerifiedUpdateAndExitAsync(VerifiedUpdatePackage package, InstallTarget target)
    {
        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Information,
            "UPDATE_INSTALL_PREPARE",
            Message: $"package={package.FilePath}, targetDir={target.TargetDirectory}, restartExe={target.RestartExecutablePath}"));

        timer.Stop();

        ApplicationCleanupResult cleanup = await controller.PrepareForApplicationExitAsync();
        if (!cleanup.SafeToExit)
        {
            log.Write(new LogEntry(
                DateTimeOffset.Now,
                LogLevel.Warning,
                "UPDATE_INSTALL_ABORTED",
                Message: $"Application not safe to exit: {cleanup.Message}"));

            MessageBox.Show(
                "程序无法安全恢复当前游戏状态，因此已取消自动安装。\n请先恢复游戏窗口或停止挂机后重试。",
                "更新已取消",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            timer.Start();
            return;
        }

        log.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Information,
            "UPDATE_INSTALL_LAUNCHED",
            Message: $"Launching installer: {package.FilePath}"));

        mainForm?.CloseForExit();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();

        string scriptPath = UpdateService.CreateRestartScript(package.FilePath, target.RestartExecutablePath);
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        Process.Start(psi);

        ExitThread();
    }

    static bool ConfirmRecovery(string prompt)
        => MessageBox.Show(prompt, "OW 助手", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}




