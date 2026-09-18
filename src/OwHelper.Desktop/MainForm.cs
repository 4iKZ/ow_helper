using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using OwHelper.Core;

namespace OwHelper.Desktop;

public sealed class MainForm : Form
{
    readonly TrayController controller;
    readonly Timer timer;
    readonly Timer pulseFlashTimer;

    // Header 控件
    readonly Label connectionDot = new Label();
    readonly Label connectionLabel = new Label();
    readonly Panel connectionBadge = new Panel();

    // 左卡片：调度与主操作
    readonly Button mainButton = new Button();
    readonly Label mainSubtitle = new Label();
    readonly PulseProgressBar progressBar = new PulseProgressBar();
    readonly Label countdownLabel = new Label();

    // 右卡片：状态指标
    readonly Label runLabel = new Label();
    readonly Label pulseLabel = new Label();
    readonly Label windowLabel = new Label();
    readonly Label resourceNote = new Label();
    readonly Label latestMessage = new Label();
    readonly Label warningLabel = new Label();
    readonly Label gpuLabel = new Label();

    // 底部操作与引导
    readonly Button bossKeyButton = new Button();
    readonly Button settingsButton = new Button();
    readonly Button guideToggleButton = new Button();
    readonly Panel guideCard = new Panel();

    int lastKnownPulseCount = -1;
    bool guideExpanded;
    bool exiting;

    public MainForm(TrayController controller)
    {
        this.controller = controller;

        Text = "OW 助手 v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(680, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        try
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe)) Icon = Icon.ExtractAssociatedIcon(exe) ?? Icon;
        }
        catch { }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 16, 22, 16),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Paper,
            AutoScroll = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Dashboard (左右双卡片)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Actions
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Guide (可折叠)

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildDashboard(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        root.Controls.Add(BuildGuideCard(), 0, 3);

        Controls.Add(root);

        // 状态定时器：每秒轮询状态
        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => RefreshStatus();
        timer.Start();

        // 脉冲高亮复位定时器
        pulseFlashTimer = new Timer { Interval = 250 };
        pulseFlashTimer.Tick += (s, e) =>
        {
            pulseFlashTimer.Stop();
            progressBar.IsFlashing = false;
        };

        RefreshStatus();
    }

    Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 12),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 左侧标题区
        var titleBox = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0),
        };

        var titleLabel = new Label
        {
            Text = "OW 助手",
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
        };

        var versionBadge = new Label
        {
            Text = "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Palette.InkSecondary,
            BackColor = Palette.Border,
            Padding = new Padding(4, 2, 4, 2),
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };

        titleBox.Controls.Add(titleLabel);
        titleBox.Controls.Add(versionBadge);

        // 右侧状态胶囊药丸 (Pill Badge)
        connectionBadge.AutoSize = true;
        connectionBadge.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        connectionBadge.Padding = new Padding(10, 5, 12, 5);
        connectionBadge.Margin = new Padding(0, 2, 0, 0);
        connectionBadge.BackColor = Palette.Panel;
        connectionBadge.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Palette.Border);
            DrawRoundedRectangle(e.Graphics, pen, new Rectangle(0, 0, connectionBadge.Width - 1, connectionBadge.Height - 1), 6);
        };

        var badgeLayout = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        connectionDot.Text = "●";
        connectionDot.Font = new Font("Segoe UI", 10f);
        connectionDot.ForeColor = Palette.StatusStopped;
        connectionDot.AutoSize = true;
        connectionDot.Margin = new Padding(0, 1, 4, 0);

        connectionLabel.Font = new Font("Segoe UI Semibold", 9f);
        connectionLabel.ForeColor = Palette.Ink;
        connectionLabel.AutoSize = true;
        connectionLabel.Margin = new Padding(0, 1, 0, 0);

        badgeLayout.Controls.Add(connectionDot);
        badgeLayout.Controls.Add(connectionLabel);
        connectionBadge.Controls.Add(badgeLayout);

        header.Controls.Add(titleBox, 0, 0);
        header.Controls.Add(connectionBadge, 1, 0);

        return header;
    }

    Control BuildDashboard()
    {
        var dashboard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 12),
        };
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));

        dashboard.Controls.Add(BuildLeftCard(), 0, 0);
        dashboard.Controls.Add(BuildRightCard(), 1, 0);
        return dashboard;
    }

    Control BuildLeftCard()
    {
        var card = CreateStyledCard();
        card.Margin = new Padding(0, 0, 8, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Palette.Panel,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 标题
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));         // 大按钮固定 56px
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16f));         // 进度条固定 16px
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 倒计时文本
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 节奏副标题

        var cardHeader = new Label
        {
            Text = "挂机调度",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        // 大操作按钮（锁定最小高度与垂直居中，确保大号字体永远完整居中）
        mainButton.Text = "开始挂机";
        mainButton.Font = new Font("Segoe UI Semibold", 13.5f);
        mainButton.Dock = DockStyle.Fill;
        mainButton.Height = 50;
        mainButton.MinimumSize = new Size(0, 50);
        mainButton.TextAlign = ContentAlignment.MiddleCenter;
        mainButton.FlatStyle = FlatStyle.Flat;
        mainButton.BackColor = Palette.Accent;
        mainButton.ForeColor = Color.White;
        mainButton.FlatAppearance.BorderSize = 0;
        mainButton.FlatAppearance.MouseOverBackColor = Palette.AccentHover;
        mainButton.Cursor = Cursors.Hand;
        mainButton.Margin = new Padding(0, 0, 0, 6);
        mainButton.Click += async (s, e) => await ToggleRunAsync();

        // 动态横向倒计时进度条
        progressBar.Dock = DockStyle.Fill;
        progressBar.Height = 10;
        progressBar.Margin = new Padding(0, 3, 0, 3);

        // 倒计时文本
        countdownLabel.Font = new Font("Segoe UI Semibold", 9.5f);
        countdownLabel.ForeColor = Palette.Accent;
        countdownLabel.AutoSize = true;
        countdownLabel.Margin = new Padding(0, 4, 0, 4);

        // 节奏副标题说明（弹性自适应填充）
        mainSubtitle.Font = new Font("Segoe UI", 9f);
        mainSubtitle.ForeColor = Palette.InkSecondary;
        mainSubtitle.AutoSize = true;
        mainSubtitle.Dock = DockStyle.Fill;
        mainSubtitle.Margin = new Padding(0, 2, 0, 0);

        layout.Controls.Add(cardHeader, 0, 0);
        layout.Controls.Add(mainButton, 0, 1);
        layout.Controls.Add(progressBar, 0, 2);
        layout.Controls.Add(countdownLabel, 0, 3);
        layout.Controls.Add(mainSubtitle, 0, 4);

        card.Controls.Add(layout);
        return card;
    }

    Control BuildRightCard()
    {
        var card = CreateStyledCard();
        card.Margin = new Padding(8, 0, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Palette.Panel,
        };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var cardHeader = new Label
        {
            Text = "实时监控",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        ConfigureValueLabel(runLabel, 10.5f, Palette.Ink, bold: true);
        ConfigureValueLabel(pulseLabel, 9f, Palette.InkSecondary);
        ConfigureValueLabel(windowLabel, 9f, Palette.InkSecondary);
        ConfigureValueLabel(resourceNote, 9f, Palette.StatusWaiting);
        ConfigureValueLabel(latestMessage, 9f, Palette.InkMuted);

        layout.Controls.Add(cardHeader, 0, 0);
        layout.Controls.Add(runLabel, 0, 1);
        layout.Controls.Add(pulseLabel, 0, 2);
        layout.Controls.Add(windowLabel, 0, 3);
        layout.Controls.Add(resourceNote, 0, 4);
        layout.Controls.Add(latestMessage, 0, 5);

        card.Controls.Add(layout);
        return card;
    }

    Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 操作按钮排
        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 8),
        };

        bossKeyButton.Text = "老板键";
        StyleModernButton(bossKeyButton, 120);
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
        StyleModernButton(settingsButton, 120);
        settingsButton.Click += (s, e) =>
        {
            using var form = new SettingsForm(controller);
            form.ShowDialog();
            RefreshStatus();
        };

        guideToggleButton.Text = "挂机指南 ▾";
        StyleModernButton(guideToggleButton, 110);
        guideToggleButton.Click += (s, e) =>
        {
            guideExpanded = !guideExpanded;
            guideToggleButton.Text = guideExpanded ? "挂机指南 ▴" : "挂机指南 ▾";
            guideCard.Visible = guideExpanded;
        };

        btnRow.Controls.Add(bossKeyButton);
        btnRow.Controls.Add(settingsButton);
        btnRow.Controls.Add(guideToggleButton);

        // 告警与 GPU 提示卡片容器（内边距舒适，结构分明）
        var noticeBox = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0),
        };

        ConfigureValueLabel(warningLabel, 8.5f, Palette.StatusWaiting);
        warningLabel.Cursor = Cursors.Hand;
        warningLabel.Click += (s, e) => controller.OpenLogFolder();

        ConfigureValueLabel(gpuLabel, 8.5f, Palette.InkSecondary);

        noticeBox.Controls.Add(warningLabel);
        noticeBox.Controls.Add(gpuLabel);

        panel.Controls.Add(btnRow, 0, 0);
        panel.Controls.Add(noticeBox, 0, 1);

        return panel;
    }

    Control BuildGuideCard()
    {
        guideCard.Dock = DockStyle.Fill;
        guideCard.BackColor = Palette.Panel;
        guideCard.Padding = new Padding(14, 12, 14, 12);
        guideCard.Margin = new Padding(0, 0, 0, 6);
        guideCard.AutoSize = true;
        guideCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        guideCard.Visible = false; // 默认折叠收拢
        guideCard.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Palette.Border);
            DrawRoundedRectangle(e.Graphics, pen, new Rectangle(0, 0, guideCard.Width - 1, guideCard.Height - 1), 6);
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

        string[] steps =
        {
            "1. 打开《守望先锋》，进入你想挂的房间或训练场",
            "2. 点上面的「开始挂机」按钮",
            "3. 正常用电脑即可，本程序在后台自动模拟按键（你玩游戏时会自动暂停）",
            "4. 想自己玩时点「停止挂机」；想立刻隐藏游戏和本程序可点「老板键」（右键托盘可恢复）",
        };

        for (int i = 0; i < steps.Length; i++)
        {
            layout.Controls.Add(new Label
            {
                Text = steps[i],
                ForeColor = Palette.InkSecondary,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                Margin = new Padding(0, 0, 0, 4),
            }, 0, i);
        }

        guideCard.Controls.Add(layout);
        return guideCard;
    }

    async Task ToggleRunAsync()
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

        // 倒计时与进度条计算
        bool running = controller.Session.IsRunning;
        string next = PlainLanguage.NextPulse(status);
        if (running && status.LastPulseAt is DateTimeOffset last)
        {
            double elapsed = (DateTimeOffset.Now - last).TotalSeconds;
            double progress = status.IntervalSec > 0 ? Math.Clamp(elapsed / status.IntervalSec, 0.0, 1.0) : 0.0;
            progressBar.Progress = progress;
            countdownLabel.Text = next.Length > 0 ? next : "准备按键…";
            countdownLabel.Visible = true;
            progressBar.Visible = true;
        }
        else
        {
            progressBar.Progress = 0.0;
            countdownLabel.Text = running ? "正在启动…" : "挂机未开始";
            countdownLabel.Visible = running;
            progressBar.Visible = running;
        }

        // 脉冲击发瞬间闪烁动效
        if (status.PulseCount > lastKnownPulseCount && lastKnownPulseCount >= 0)
        {
            progressBar.IsFlashing = true;
            pulseFlashTimer.Stop();
            pulseFlashTimer.Start();
        }
        lastKnownPulseCount = status.PulseCount;

        windowLabel.Text = PlainLanguage.WindowInfo(status, controller.Session.IsOffscreen);
        resourceNote.Text = PlainLanguage.ResourceNote(status);
        resourceNote.Visible = resourceNote.Text.Length > 0;
        latestMessage.Text = AppMessages.Latest.Length > 0 ? "提示：" + AppMessages.Latest : "";
        latestMessage.Visible = latestMessage.Text.Length > 0;

        warningLabel.Text = controller.StartupProblems.Count == 0
            ? ""
            : $"⚠ 有 {controller.StartupProblems.Count} 项设置已自动修正（点我查看运行记录）";
        warningLabel.Visible = warningLabel.Text.Length > 0;
        gpuLabel.Text = controller.GpuGuide ?? "";
        gpuLabel.Visible = gpuLabel.Text.Length > 0;

        string rhythm = $"{string.Join("、", controller.Config.Input.Keys)} · 每 {controller.Config.Input.IntervalSeconds} 秒";
        mainButton.Text = running ? "停止挂机" : "开始挂机";
        mainButton.BackColor = running ? Palette.StatusFaulted : Palette.Accent;
        mainButton.FlatAppearance.MouseOverBackColor = running ? Palette.StatusFaulted : Palette.AccentHover;
        mainSubtitle.Text = running
            ? $"正在自动调度按键（{rhythm}）"
            : $"预设：{rhythm}（点「更多设置」可自定义）" + (status.Pid == null ? "；尚未检测到游戏" : "");

        bossKeyButton.Text = controller.Session.IsOffscreen ? "恢复游戏窗口" : "老板键";

        connectionBadge.Invalidate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(680 * DeviceDpi / 96, work.Width - 40);
        int height = Math.Min(540 * DeviceDpi / 96, work.Height - 40);
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
        pulseFlashTimer.Stop();
        Close();
    }

    static Panel CreateStyledCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Panel,
            Padding = new Padding(16, 14, 16, 14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        card.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Palette.Border);
            DrawRoundedRectangle(e.Graphics, pen, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6);
        };
        return card;
    }

    static void ConfigureValueLabel(Label label, float size, Color color, bool bold = false)
    {
        label.AutoSize = true;
        label.Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size);
        label.ForeColor = color;
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 0, 0, 5);
    }

    static void StyleModernButton(Button button, int width)
    {
        button.Size = new Size(width, 34);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Palette.Panel;
        button.ForeColor = Palette.Ink;
        button.Font = new Font("Segoe UI", 9f);
        button.FlatAppearance.BorderColor = Palette.Border;
        button.FlatAppearance.MouseOverBackColor = Palette.SurfaceSubtle;
        button.FlatAppearance.MouseDownBackColor = Palette.AccentWash;
        button.Cursor = Cursors.Hand;
    }

    static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var size = new Size(diameter, diameter);
        var arc = new Rectangle(bounds.Location, size);
        using var path = new GraphicsPath();

        if (radius == 0)
        {
            path.AddRectangle(bounds);
        }
        else
        {
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
        }

        g.DrawPath(pen, path);
    }
}
