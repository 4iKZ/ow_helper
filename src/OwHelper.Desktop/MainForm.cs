using System;
using System.Drawing;
using System.Windows.Forms;
using OwHelper.Core;

namespace OwHelper.Desktop;

public sealed class MainForm : Form
{
    readonly TrayController controller;
    readonly Timer timer;
    readonly Label connectionDot = new Label();
    readonly Label connectionLabel = ValueLabel();
    readonly Label runLabel = ValueLabel();
    readonly Label pulseLabel = ValueLabel();
    readonly Label windowLabel = ValueLabel();
    readonly Label resourceNote = ValueLabel();
    readonly Button mainButton = new Button();
    readonly Label mainSubtitle = ValueLabel();
    readonly Button offscreenButton = ActionButton();
    bool exiting;

    public MainForm(TrayController controller)
    {
        this.controller = controller;

        Text = "OW 助手";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(720, 620);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 16),
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStatusCard(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        root.Controls.Add(BuildGuide(), 0, 3);

        Controls.Add(root);

        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => RefreshStatus();
        timer.Start();

        RefreshStatus();
    }

    Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Paper };
        panel.Controls.Add(new Label
        {
            Text = "OW 助手",
            Font = new Font("Segoe UI Semibold", 17f),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Location = new Point(0, 6),
        });
        panel.Controls.Add(new Label
        {
            Text = "让《守望先锋》在后台自动按键，你可以放心用电脑做别的事",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            Location = new Point(2, 38),
        });
        return panel;
    }

    Control BuildStatusCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Panel,
            Padding = new Padding(18, 14, 18, 12),
            Margin = new Padding(0, 0, 0, 12),
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        connectionDot.Text = "●";
        connectionDot.Font = new Font("Segoe UI", 12f);
        connectionDot.ForeColor = Palette.StatusStopped;
        connectionDot.AutoSize = true;
        connectionDot.Location = new Point(16, 16);

        connectionLabel.Font = new Font("Segoe UI Semibold", 12f);
        connectionLabel.AutoSize = true;
        connectionLabel.Location = new Point(40, 18);
        connectionLabel.ForeColor = Palette.Ink;

        runLabel.AutoSize = true;
        runLabel.Location = new Point(18, 52);
        runLabel.Font = new Font("Segoe UI", 10.5f);
        runLabel.ForeColor = Palette.Ink;

        pulseLabel.AutoSize = true;
        pulseLabel.Location = new Point(18, 78);
        pulseLabel.ForeColor = Palette.InkSecondary;

        windowLabel.AutoSize = true;
        windowLabel.Location = new Point(18, 100);
        windowLabel.ForeColor = Palette.InkSecondary;

        resourceNote.AutoSize = true;
        resourceNote.Location = new Point(18, 122);
        resourceNote.ForeColor = Palette.StatusWaiting;

        card.Controls.Add(connectionDot);
        card.Controls.Add(connectionLabel);
        card.Controls.Add(runLabel);
        card.Controls.Add(pulseLabel);
        card.Controls.Add(windowLabel);
        card.Controls.Add(resourceNote);
        return card;
    }

    Control BuildActions()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Paper };

        mainButton.Text = "托比昂战令一键启动";
        mainButton.Font = new Font("Segoe UI Semibold", 15f);
        mainButton.Size = new Size(420, 62);
        mainButton.Location = new Point(2, 4);
        mainButton.FlatStyle = FlatStyle.Flat;
        mainButton.BackColor = Palette.Accent;
        mainButton.ForeColor = Palette.Panel;
        mainButton.FlatAppearance.BorderSize = 0;
        mainButton.FlatAppearance.MouseOverBackColor = Palette.AccentHover;
        mainButton.Click += async (s, e) => await ToggleRunAsync();

        mainSubtitle.AutoSize = true;
        mainSubtitle.Location = new Point(4, 72);
        mainSubtitle.ForeColor = Palette.InkSecondary;
        mainSubtitle.Text = "每 30 秒自动按一次 Shift"; 

        offscreenButton.Text = "隐藏游戏窗口";
        offscreenButton.Location = new Point(440, 18);
        offscreenButton.Size = new Size(160, 38);
        offscreenButton.Click += async (s, e) =>
        {
            await controller.ToggleOffscreenAsync();
            RefreshStatus();
        };

        var settingsButton = ActionButton();
        settingsButton.Text = "更多设置…";
        settingsButton.Location = new Point(440, 62);
        settingsButton.Size = new Size(160, 30);
        settingsButton.Click += (s, e) =>
        {
            using var form = new SettingsForm(controller);
            form.ShowDialog();
            RefreshStatus();
        };

        panel.Controls.Add(mainButton);
        panel.Controls.Add(mainSubtitle);
        panel.Controls.Add(offscreenButton);
        panel.Controls.Add(settingsButton);
        return panel;
    }

    Control BuildGuide()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Panel,
            Padding = new Padding(18, 14, 18, 12),
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        card.Controls.Add(new Label
        {
            Text = "怎么用（四步）",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Palette.Accent,
            AutoSize = true,
            Location = new Point(16, 12),
        });

        string[] steps =
        {
            "1. 打开《守望先锋》，进入你想挂的房间或训练场",
            "2. 点上面的「托比昂战令一键启动」",
            "3. 正常用电脑就行，它会在后台自动按键（你玩游戏时会自动暂停）",
            "4. 想自己玩时点一次上面的按钮停止；不想看见游戏窗口可以点「隐藏游戏窗口」",
        };
        int y = 40;
        foreach (string step in steps)
        {
            card.Controls.Add(new Label
            {
                Text = step,
                ForeColor = Palette.Ink,
                AutoSize = true,
                Location = new Point(18, y),
            });
            y += 26;
        }
        return card;
    }

    async System.Threading.Tasks.Task ToggleRunAsync()
    {
        if (controller.Session.IsRunning)
        {
            await controller.StopAsync();
        }
        else
        {
            QuickPresets.Apply(QuickPresets.TorbjornPass, controller.Config);
            await controller.ApplySettingsAsync(controller.Config);
            await controller.StartAsync();
        }
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        TrayStatus status = controller.Snapshot();

        connectionDot.ForeColor = TrayStatusMapper.StatusColor(status);
        connectionLabel.Text = PlainLanguage.Connection(status);
        runLabel.Text = PlainLanguage.RunState(status);
        pulseLabel.Text = PlainLanguage.PulseSummary(status);

        string next = PlainLanguage.NextPulse(status);
        if (next.Length > 0) pulseLabel.Text += " · " + next;

        windowLabel.Text = PlainLanguage.WindowInfo(status);
        resourceNote.Text = PlainLanguage.ResourceNote(status);

        bool running = controller.Session.IsRunning;
        mainButton.Text = running ? "停止挂机" : "托比昂战令一键启动";
        mainButton.BackColor = running ? Palette.StatusFaulted : Palette.Accent;
        mainButton.FlatAppearance.MouseOverBackColor = running ? Palette.StatusFaulted : Palette.AccentHover;
        mainSubtitle.Text = running
            ? "正在自动按键；按键和间隔可在「更多设置」里调整"
            : QuickPresets.TorbjornPass.Subtitle + (status.Pid == null ? "（先打开《守望先锋》）" : "");
        offscreenButton.Text = controller.Session.IsOffscreen ? "显示游戏窗口" : "隐藏游戏窗口";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    public event Action? HiddenToTray;

    public void CloseForExit()
    {
        exiting = true;
        timer.Stop();
        Close();
    }

    static Label ValueLabel() => new Label
    {
        AutoSize = true,
        ForeColor = Palette.InkSecondary,
    };

    static Button ActionButton() => new Button
    {
        FlatStyle = FlatStyle.Flat,
        BackColor = Palette.Panel,
        ForeColor = Palette.Ink,
        FlatAppearance =
        {
            BorderColor = Palette.Border,
            MouseOverBackColor = Palette.AccentWash,
            MouseDownBackColor = Palette.AccentWash,
        },
    };
}
