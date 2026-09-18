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
    readonly Label versionBadge = new Label();
    UpdateInfo? cachedUpdateInfo;

    // 左卡片：调度与主操作
    readonly RoundedButton mainButton = new RoundedButton();
    readonly Label mainSubtitle = new Label();
    readonly PulseProgressBar progressBar = new PulseProgressBar();
    readonly Label countdownLabel = new Label();

    // 右卡片：现代 2x2 指标卡片与状态
    readonly MetricTile tileRunStatus = new MetricTile { Title = "运行状态", ValueFontSize = 10f };
    readonly MetricTile tileWindowStatus = new MetricTile { Title = "目标窗口", ValueFontSize = 10f };
    readonly MetricTile tilePulseCount = new MetricTile { Title = "本次击发", ValueColor = Palette.Accent, ValueFontSize = 13f };
    readonly MetricTile tileDuration = new MetricTile { Title = "挂机时长", ValueFontSize = 11.5f };
    readonly Label resourceNote = new Label();
    readonly Label latestMessage = new Label();
    readonly Label warningLabel = new Label();
    readonly Label gpuLabel = new Label();

    // 底部操作与引导
    readonly RoundedButton bossKeyButton = new RoundedButton();
    readonly RoundedButton settingsButton = new RoundedButton();
    readonly RoundedButton guideToggleButton = new RoundedButton();
    readonly Panel guideCard = new Panel();

    readonly DoubleBufferedTableLayoutPanel rootLayout;
    int lastKnownPulseCount = -1;
    bool guideExpanded;
    bool exiting;

    public MainForm(TrayController controller)
    {
        this.controller = controller;

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;

        Text = "OW 助手 v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9f);
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

        rootLayout = new DoubleBufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 16, 22, 16),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Paper,
            AutoScroll = true,
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Dashboard (左右双卡片)
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Actions
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Guide (可折叠)

        rootLayout.Controls.Add(BuildHeader(), 0, 0);
        rootLayout.Controls.Add(BuildDashboard(), 0, 1);
        rootLayout.Controls.Add(BuildActions(), 0, 2);
        rootLayout.Controls.Add(BuildGuideCard(), 0, 3);

        Controls.Add(rootLayout);

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

        if (controller.Config.Update.AutoCheckOnStartup)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await CheckForUpdatesAsync(manual: false);
            });
        }
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
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
        };

        string currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        versionBadge.Text = "v" + currentVer;
        versionBadge.Font = new Font("Microsoft YaHei UI", 8.5f);
        versionBadge.ForeColor = Palette.InkSecondary;
        versionBadge.BackColor = Palette.Border;
        versionBadge.Padding = new Padding(5, 2, 5, 2);
        versionBadge.AutoSize = true;
        versionBadge.Margin = new Padding(0, 5, 0, 0);
        versionBadge.Cursor = Cursors.Hand;
        var tt = new ToolTip();
        tt.SetToolTip(versionBadge, "点击检查新版本");
        versionBadge.Click += async (s, e) => await CheckForUpdatesAsync(manual: true);

        titleBox.Controls.Add(titleLabel);
        titleBox.Controls.Add(versionBadge);

        // 右侧状态胶囊药丸 (Pill Badge)
        connectionBadge.AutoSize = true;
        connectionBadge.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        connectionBadge.Padding = new Padding(10, 5, 12, 5);
        connectionBadge.Margin = new Padding(0, 2, 0, 0);
        connectionBadge.BackColor = Palette.Paper;
        connectionBadge.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float strokeInset = 0.5f;
            var rectF = new RectangleF(strokeInset, strokeInset, Math.Max(1f, connectionBadge.Width - 1f), Math.Max(1f, connectionBadge.Height - 1f));
            using var path = GetRoundedRectanglePathF(rectF, 6);
            using var bgBrush = new SolidBrush(Palette.Panel);
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.FillPath(bgBrush, path);
            e.Graphics.DrawPath(pen, path);
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
        connectionDot.Font = new Font("Microsoft YaHei UI", 9f);
        connectionDot.ForeColor = Palette.StatusStopped;
        connectionDot.AutoSize = true;
        connectionDot.Margin = new Padding(0, 1, 4, 0);

        connectionLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
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
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        dashboard.Controls.Add(BuildLeftCard(), 0, 0);
        dashboard.Controls.Add(BuildRightCard(), 1, 0);
        return dashboard;
    }

    Control BuildLeftCard()
    {
        var card = CreateStyledCard();
        card.Margin = new Padding(0, 0, 6, 0);

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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 进度条 (运行期自适应)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 倒计时文本
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));             // 节奏副标题

        var cardHeader = new Label
        {
            Text = "挂机调度",
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        // 大操作按钮（圆角微动效，字体绝对居中）
        mainButton.Text = "开始挂机";
        mainButton.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
        mainButton.Dock = DockStyle.Fill;
        mainButton.Height = 52;
        mainButton.MinimumSize = new Size(0, 50);
        mainButton.CornerRadius = 8;
        mainButton.BorderSize = 0;
        mainButton.BackColor = Palette.Accent;
        mainButton.ForeColor = Color.White;
        mainButton.HoverBackColor = Palette.AccentHover;
        mainButton.PressedBackColor = Palette.AccentHover;
        mainButton.Margin = new Padding(0, 0, 0, 6);
        mainButton.Click += async (s, e) => await ToggleRunAsync();

        // 动态横向倒计时进度条
        progressBar.Dock = DockStyle.Fill;
        progressBar.Height = 10;
        progressBar.Margin = new Padding(0, 4, 0, 4);

        // 倒计时文本（水平垂直严格居中）
        countdownLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        countdownLabel.ForeColor = Palette.Accent;
        countdownLabel.Dock = DockStyle.Fill;
        countdownLabel.TextAlign = ContentAlignment.MiddleCenter;
        countdownLabel.AutoSize = false;
        countdownLabel.Height = 22;
        countdownLabel.Margin = new Padding(0, 2, 0, 4);

        // 节奏副标题说明（水平垂直严格居中，容纳两行折行说明）
        mainSubtitle.Font = new Font("Microsoft YaHei UI", 9f);
        mainSubtitle.ForeColor = Palette.InkSecondary;
        mainSubtitle.Dock = DockStyle.Fill;
        mainSubtitle.TextAlign = ContentAlignment.MiddleCenter;
        mainSubtitle.AutoSize = false;
        mainSubtitle.Height = 38;
        mainSubtitle.Margin = new Padding(0, 6, 0, 0);

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
        card.Margin = new Padding(6, 0, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Panel,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 标题
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2x2 指标网格
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 资源附注
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 最新消息

        var cardHeader = new Label
        {
            Text = "实时监控",
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        // 2x2 现代指标卡片网格
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Palette.Panel,
            Margin = new Padding(0, 0, 0, 6),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));

        tileRunStatus.Dock = DockStyle.Fill;
        tileRunStatus.Margin = new Padding(3);
        tileWindowStatus.Dock = DockStyle.Fill;
        tileWindowStatus.Margin = new Padding(3);
        tilePulseCount.Dock = DockStyle.Fill;
        tilePulseCount.Margin = new Padding(3);
        tileDuration.Dock = DockStyle.Fill;
        tileDuration.Margin = new Padding(3);

        grid.Controls.Add(tileRunStatus, 0, 0);
        grid.Controls.Add(tileWindowStatus, 1, 0);
        grid.Controls.Add(tilePulseCount, 0, 1);
        grid.Controls.Add(tileDuration, 1, 1);

        ConfigureValueLabel(resourceNote, 8.5f, Palette.StatusWaiting);
        ConfigureValueLabel(latestMessage, 8.5f, Palette.InkMuted);

        layout.Controls.Add(cardHeader, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(resourceNote, 0, 2);
        layout.Controls.Add(latestMessage, 0, 3);

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
        guideCard.BackColor = Palette.Paper;
        guideCard.Padding = new Padding(14, 12, 14, 12);
        guideCard.Margin = new Padding(0, 0, 0, 6);
        guideCard.AutoSize = true;
        guideCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        guideCard.Visible = false; // 默认折叠收拢
        guideCard.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float strokeInset = 0.5f;
            var rectF = new RectangleF(strokeInset, strokeInset, Math.Max(1f, guideCard.Width - 1f), Math.Max(1f, guideCard.Height - 1f));
            using var path = GetRoundedRectanglePathF(rectF, 6);
            using var bgBrush = new SolidBrush(Palette.Panel);
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.FillPath(bgBrush, path);
            e.Graphics.DrawPath(pen, path);
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
                Font = new Font("Microsoft YaHei UI", 8.5f),
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

        bool running = controller.Session.IsRunning;

        // 1. 运行状态
        tileRunStatus.ValueText = PlainLanguage.RunState(status);
        tileRunStatus.ValueColor = running ? Palette.Accent : Palette.Ink;

        // 2. 目标窗口
        if (controller.Session.IsOffscreen)
        {
            tileWindowStatus.ValueText = "已隐藏";
            tileWindowStatus.ValueColor = Palette.StatusWaiting;
        }
        else if (status.Pid == null)
        {
            tileWindowStatus.ValueText = "未连接";
            tileWindowStatus.ValueColor = Palette.InkMuted;
        }
        else if (status.Minimized)
        {
            tileWindowStatus.ValueText = "已最小化";
            tileWindowStatus.ValueColor = Palette.InkSecondary;
        }
        else if (status.Width > 0)
        {
            tileWindowStatus.ValueText = $"{status.Width}×{status.Height}";
            tileWindowStatus.ValueColor = Palette.Ink;
        }
        else
        {
            tileWindowStatus.ValueText = "已连接";
            tileWindowStatus.ValueColor = Palette.Ink;
        }

        // 3. 击发次数
        tilePulseCount.ValueText = $"{status.RunPulseCount} 次";

        // 4. 挂机时长
        if (running && status.RunStartedAt is DateTimeOffset started)
        {
            var elapsed = DateTimeOffset.Now - started;
            tileDuration.ValueText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}时{elapsed.Minutes}分"
                : $"{(int)elapsed.TotalMinutes} 分钟";
        }
        else
        {
            tileDuration.ValueText = "--";
        }

        // 倒计时与进度条计算
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
        mainButton.HoverBackColor = running ? Palette.StatusFaulted : Palette.AccentHover;
        mainButton.PressedBackColor = running ? Palette.StatusFaulted : Palette.AccentHover;
        mainSubtitle.Text = running
            ? $"正在自动调度按键（{rhythm}）"
            : $"预设：{rhythm}（点「更多设置」可自定义）" + (status.Pid == null ? "\n尚未检测到游戏" : "");

        bossKeyButton.Text = controller.Session.IsOffscreen ? "恢复游戏窗口" : "老板键";

        connectionBadge.Invalidate();
        rootLayout.Invalidate(true);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(680, work.Width - 20);
        int height = Math.Min(540, work.Height - 20);
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
            BackColor = Palette.Paper,
            Padding = new Padding(16, 14, 16, 14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        card.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float strokeInset = 0.5f;
            var rectF = new RectangleF(strokeInset, strokeInset, Math.Max(1f, card.Width - 1f), Math.Max(1f, card.Height - 1f));
            using var path = GetRoundedRectanglePathF(rectF, 6);
            using var bgBrush = new SolidBrush(Palette.Panel);
            using var pen = new Pen(Palette.Border, 1);
            g.FillPath(bgBrush, path);
            g.DrawPath(pen, path);
        };
        return card;
    }

    sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
    {
        public DoubleBufferedTableLayoutPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            DoubleBuffered = true;
        }
    }

    static GraphicsPath GetRoundedRectanglePathF(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2f;
        if (diameter <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void ConfigureValueLabel(Label label, float size, Color color, bool bold = false)
    {
        label.AutoSize = true;
        label.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        label.ForeColor = color;
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 0, 0, 4);
    }

    static void StyleModernButton(RoundedButton button, int width)
    {
        button.Size = new Size(width, 36);
        button.Margin = new Padding(0, 0, 8, 0);
        button.CornerRadius = 6;
        button.BorderSize = 1;
        button.BorderColor = Palette.Border;
        button.BackColor = Palette.Panel;
        button.ForeColor = Palette.Ink;
        button.Font = new Font("Microsoft YaHei UI", 9f);
        button.HoverBackColor = Palette.SurfaceSubtle;
        button.PressedBackColor = Palette.AccentWash;
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

    public Task TriggerCheckForUpdatesAsync(bool manual = true)
        => CheckForUpdatesAsync(manual);

    async Task CheckForUpdatesAsync(bool manual)
    {
        string currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        try
        {
            if (manual && cachedUpdateInfo == null)
            {
                SafeInvoke(() => versionBadge.Text = "正在检查更新…");
            }

            UpdateInfo? update = cachedUpdateInfo ?? await controller.UpdateService.CheckForUpdatesAsync(currentVer);
            if (IsDisposed) return;

            if (update != null)
            {
                cachedUpdateInfo = update;
                SafeInvoke(() =>
                {
                    versionBadge.Text = $"✨ 发现新版 v{update.Version} (点击更新)";
                    versionBadge.BackColor = Palette.AccentWash;
                    versionBadge.ForeColor = Palette.Accent;
                    if (manual)
                    {
                        using var dialog = new UpdateDialog(controller, update, currentVer);
                        dialog.ShowDialog(this);
                    }
                });
            }
            else
            {
                SafeInvoke(() =>
                {
                    versionBadge.Text = "v" + currentVer;
                    versionBadge.BackColor = Palette.Border;
                    versionBadge.ForeColor = Palette.InkSecondary;
                    if (manual)
                    {
                        MessageBox.Show(this, $"当前版本 v{currentVer} 已是最新版本！", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SafeInvoke(() =>
            {
                versionBadge.Text = "v" + currentVer;
                if (manual)
                {
                    MessageBox.Show(this, $"检查更新失败: {ex.Message}\n请检查网络连接或稍后重试。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        }
    }

    void SafeInvoke(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { Invoke(action); } catch (ObjectDisposedException) { }
        }
        else
        {
            action();
        }
    }
}
