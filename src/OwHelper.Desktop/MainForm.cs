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
    readonly Label connectionLabel = new Label();
    readonly Label runLabel = new Label();
    readonly Label pulseLabel = new Label();
    readonly Label windowLabel = new Label();
    readonly Label resourceNote = new Label();
    readonly Label latestMessage = new Label();
    readonly Label warningLabel = new Label();
    readonly Label gpuLabel = new Label();
    readonly Button mainButton = new Button();
    readonly Label mainSubtitle = new Label();
    readonly Button bossKeyButton = new Button();
    readonly Button settingsButton = new Button();
    bool exiting;

    public MainForm(TrayController controller)
    {
        this.controller = controller;

        Text = "OW 助手";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(900, 800);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Paper,
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 14),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "OW 助手",
            Font = new Font("Segoe UI Semibold", 18f),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "让《守望先锋》在后台自动按键，你可以放心用电脑做别的事",
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            MaximumSize = new Size(830, 0),
            Margin = new Padding(2, 0, 0, 0),
        }, 0, 1);

        return layout;
    }

    Control BuildStatusCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Panel,
            Padding = new Padding(20, 16, 20, 16),
            Margin = new Padding(0, 0, 0, 16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Panel,
        };
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var connectionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Palette.Panel,
        };
        connectionDot.Text = "●";
        connectionDot.Font = new Font("Segoe UI", 13f);
        connectionDot.ForeColor = Palette.StatusStopped;
        connectionDot.AutoSize = true;
        connectionDot.Margin = new Padding(0, 2, 8, 0);
        connectionLabel.Font = new Font("Segoe UI Semibold", 13f);
        connectionLabel.ForeColor = Palette.Ink;
        connectionLabel.AutoSize = true;
        connectionLabel.Margin = new Padding(0, 0, 0, 0);
        connectionRow.Controls.Add(connectionDot);
        connectionRow.Controls.Add(connectionLabel);

        ConfigureValueLabel(runLabel, 11f, Palette.Ink);
        ConfigureValueLabel(pulseLabel, 9.5f, Palette.InkSecondary);
        ConfigureValueLabel(windowLabel, 9.5f, Palette.InkSecondary);
        ConfigureValueLabel(resourceNote, 9.5f, Palette.StatusWaiting);
        ConfigureValueLabel(latestMessage, 9.5f, Palette.InkSecondary);
        ConfigureValueLabel(warningLabel, 9.5f, Palette.StatusWaiting);
        ConfigureValueLabel(gpuLabel, 9.5f, Palette.InkSecondary);
        warningLabel.Cursor = Cursors.Hand;
        warningLabel.Click += (s, e) => controller.OpenLogFolder();

        layout.Controls.Add(connectionRow, 0, 0);
        layout.Controls.Add(runLabel, 0, 1);
        layout.Controls.Add(pulseLabel, 0, 2);
        layout.Controls.Add(windowLabel, 0, 3);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Palette.Panel,
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.Controls.Add(layout, 0, 0);
        outer.Controls.Add(resourceNote, 0, 1);
        outer.Controls.Add(latestMessage, 0, 2);
        outer.Controls.Add(warningLabel, 0, 3);
        outer.Controls.Add(gpuLabel, 0, 4);
        card.Controls.Add(outer);
        return card;
    }

    Control BuildActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var left = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Paper,
            Margin = new Padding(0),
        };
        for (int i = 0; i < 3; i++) left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        mainButton.Text = "开始挂机";
        mainButton.Font = new Font("Segoe UI Semibold", 16f);
        mainButton.AutoSize = true;
        mainButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        mainButton.Padding = new Padding(28, 0, 28, 0);
        mainButton.MinimumSize = new Size(480, 68);
        mainButton.Margin = new Padding(0, 0, 0, 10);
        mainButton.FlatStyle = FlatStyle.Flat;
        mainButton.BackColor = Palette.Accent;
        mainButton.ForeColor = Palette.Panel;
        mainButton.FlatAppearance.BorderSize = 0;
        mainButton.FlatAppearance.MouseOverBackColor = Palette.AccentHover;
        mainButton.Click += async (s, e) => await ToggleRunAsync();

        mainSubtitle.AutoSize = true;
        mainSubtitle.ForeColor = Palette.InkSecondary;
        mainSubtitle.MaximumSize = new Size(590, 0);
        mainSubtitle.Margin = new Padding(2, 0, 0, 0);

        left.Controls.Add(mainButton, 0, 0);
        left.Controls.Add(mainSubtitle, 0, 1);
        left.Controls.Add(new Label
        {
            Text = "想自己玩时，再点一次同一个按钮即可停止",
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            MaximumSize = new Size(590, 0),
            Margin = new Padding(2, 6, 0, 0),
        }, 0, 2);

        var right = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Paper,
            Margin = new Padding(16, 0, 0, 0),
        };
        for (int i = 0; i < 3; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        bossKeyButton.Text = "老板键";
        StyleSideButton(bossKeyButton);
        bossKeyButton.Click += async (s, e) =>
        {
            bool wasHidden = controller.Session.IsOffscreen;
            await controller.ToggleOffscreenAsync();
            RefreshStatus();
            if (!wasHidden && controller.Session.IsOffscreen)
            {
                Hide();
                BossKeyUsed?.Invoke();
            }
        };

        settingsButton.Text = "更多设置…";
        StyleSideButton(settingsButton);
        settingsButton.Click += (s, e) =>
        {
            using var form = new SettingsForm(controller);
            form.ShowDialog();
            RefreshStatus();
        };

        right.Controls.Add(bossKeyButton, 0, 0);
        right.Controls.Add(settingsButton, 0, 1);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        return layout;
    }

    Control BuildGuide()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Panel,
            Padding = new Padding(20, 16, 20, 16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Palette.Panel,
        };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "怎么用（四步）",
            Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = Palette.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 0);

        string[] steps =
        {
            "1. 打开《守望先锋》，进入你想挂的房间或训练场",
            "2. 点上面的「开始挂机」",
            "3. 正常用电脑就行，它会在后台自动按键（你玩游戏时会自动暂停）",
            "4. 想自己玩时点一次上面的按钮停止；想立刻「消失」可以点「老板键」",
            "   （游戏和本窗口都会隐藏，右键托盘图标即可恢复）",
        };
        for (int i = 0; i < steps.Length; i++)
        {
            layout.Controls.Add(new Label
            {
                Text = steps[i],
                ForeColor = Palette.Ink,
                AutoSize = true,
                MaximumSize = new Size(800, 0),
                Margin = new Padding(2, 0, 0, 8),
            }, 0, i + 1);
        }

        card.Controls.Add(layout);
        return card;
    }

    async System.Threading.Tasks.Task ToggleRunAsync()
    {
        if (controller.Session.IsRunning) await controller.StopAsync();
        else await controller.StartAsync();
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

        windowLabel.Text = PlainLanguage.WindowInfo(status, controller.Session.IsOffscreen);
        resourceNote.Text = PlainLanguage.ResourceNote(status);
        latestMessage.Text = AppMessages.Latest.Length > 0 ? "最近：" + AppMessages.Latest : "";
        warningLabel.Text = controller.StartupProblems.Count == 0
            ? ""
            : $"⚠ 有 {controller.StartupProblems.Count} 项设置已自动修正（点我查看运行记录）";
        warningLabel.Visible = warningLabel.Text.Length > 0;
        gpuLabel.Text = controller.GpuGuide ?? "";
        gpuLabel.Visible = gpuLabel.Text.Length > 0;

        bool running = controller.Session.IsRunning;
        string rhythm = $"{string.Join("、", controller.Config.Input.Keys)} · 每 {controller.Config.Input.IntervalSeconds} 秒";
        mainButton.Text = running ? "停止挂机" : "开始挂机";
        mainButton.BackColor = running ? Palette.StatusFaulted : Palette.Accent;
        mainButton.FlatAppearance.MouseOverBackColor = running ? Palette.StatusFaulted : Palette.AccentHover;
        mainSubtitle.Text = running
            ? $"正在自动按键（{rhythm}）"
            : $"当前：{rhythm}（点「更多设置」可改）" + (status.Pid == null ? "；先打开《守望先锋》" : "");
        bossKeyButton.Text = controller.Session.IsOffscreen ? "恢复游戏窗口" : "老板键";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(900 * DeviceDpi / 96, work.Width - 40);
        int height = Math.Min(800 * DeviceDpi / 96, work.Height - 40);
        ClientSize = new Size(width, height);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
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

    public event Action? BossKeyUsed;

    public void CloseForExit()
    {
        exiting = true;
        timer.Stop();
        Close();
    }

    static void ConfigureValueLabel(Label label, float size, Color color)
    {
        label.AutoSize = true;
        label.Font = new Font("Segoe UI", size);
        label.ForeColor = color;
        label.MaximumSize = new Size(820, 0);
        label.Margin = new Padding(0, 0, 0, 6);
    }

    static void StyleSideButton(Button button)
    {
        button.Size = new Size(200, 36);
        button.Margin = new Padding(0, 0, 0, 12);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Palette.Panel;
        button.ForeColor = Palette.Ink;
        button.FlatAppearance.BorderColor = Palette.Border;
        button.FlatAppearance.MouseOverBackColor = Palette.AccentWash;
        button.FlatAppearance.MouseDownBackColor = Palette.AccentWash;
    }
}
