using System;
using System.Drawing;
using System.Windows.Forms;

namespace OwHelper.Tray;

public sealed class StatusForm : Form
{
    readonly TrayController controller;
    readonly Timer timer;
    readonly Label stateValue = ValueLabel();
    readonly Label targetValue = ValueLabel();
    readonly Label aliveValue = ValueLabel();
    readonly Label intervalValue = ValueLabel();
    readonly Label pulsesValue = ValueLabel();
    readonly Label logValue = ValueLabel();
    readonly Label gpuValue = ValueLabel();
    readonly Label gpuGuideValue = ValueLabel();
    readonly Button toggleButton = ActionButton();
    readonly Button offscreenButton = ActionButton();
    readonly Button logsButton = ActionButton();
    bool exiting;

    public StatusForm(TrayController controller)
    {
        this.controller = controller;

        Text = "OW Helper 状态";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(620, 332);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 8),
            ColumnCount = 2,
            RowCount = 8,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        AddRow(grid, 0, "状态", stateValue);
        AddRow(grid, 1, "目标", targetValue);
        AddRow(grid, 2, "目标有效", aliveValue);
        AddRow(grid, 3, "间隔", intervalValue);
        AddRow(grid, 4, "脉冲", pulsesValue);
        AddRow(grid, 5, "日志", logValue);
        AddRow(grid, 6, "GPU", gpuValue);
        AddRow(grid, 7, "GPU 策略", gpuGuideValue);

        toggleButton.Text = "开始";
        toggleButton.Click += async (s, e) =>
        {
            if (controller.Session.IsRunning) await controller.StopAsync();
            else await controller.StartAsync();
            RefreshStatus(controller.Snapshot());
        };
        offscreenButton.Text = "移出屏幕";
        offscreenButton.Click += async (s, e) =>
        {
            await controller.ToggleOffscreenAsync();
            RefreshStatus(controller.Snapshot());
        };
        logsButton.Text = "打开日志";
        logsButton.Click += (s, e) => controller.OpenLogFolder();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(18, 0, 18, 16),
            Height = 56,
            BackColor = Palette.Paper,
        };
        buttons.Controls.Add(toggleButton);
        buttons.Controls.Add(offscreenButton);
        buttons.Controls.Add(logsButton);

        Controls.Add(grid);
        Controls.Add(buttons);

        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => RefreshStatus(controller.Snapshot());
        timer.Start();
    }

    public void RefreshStatus(TrayStatus status)
    {
        stateValue.Text = TrayStatusMapper.StateLabel(status.State);
        stateValue.ForeColor = TrayStatusMapper.StatusColor(status);
        stateValue.Font = new Font("Segoe UI Semibold", 11f);
        targetValue.Text = status.Pid is int pid
            ? $"PID {pid}   HWND 0x{status.Hwnd.ToInt64():X8}   {status.Width}x{status.Height}"
            : "（未检测到）";
        aliveValue.Text = status.TargetAlive ? "是" : "否";
        aliveValue.ForeColor = status.TargetAlive ? Palette.StatusRunning : Palette.StatusFaulted;
        intervalValue.Text = $"{status.IntervalSec}s（jitter {status.JitterPercent}%）";
        pulsesValue.Text = status.LastPulseAt is DateTimeOffset at
            ? $"{status.PulseCount}（{TrayStatusMapper.Ago(at)}）"
            : status.PulseCount.ToString();
        logValue.Text = status.LogPath;
        gpuValue.Text = string.IsNullOrEmpty(status.GpuSummary) ? "未检测到" : status.GpuSummary;
        gpuGuideValue.Text = string.IsNullOrEmpty(status.GpuGuidance) ? "—" : status.GpuGuidance;
        gpuGuideValue.ForeColor = Palette.InkSecondary;
        toggleButton.Text = controller.Session.IsRunning ? "停止" : "开始";
        offscreenButton.Text = controller.Session.IsOffscreen ? "还原窗口" : "移出屏幕";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void CloseForExit()
    {
        exiting = true;
        timer.Stop();
        Close();
    }

    static void AddRow(TableLayoutPanel grid, int row, string caption, Label value)
    {
        var label = new Label
        {
            Text = caption,
            ForeColor = Palette.InkSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    static Label ValueLabel() => new Label
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Palette.Ink,
        AutoSize = false,
        AutoEllipsis = true,
    };

    static Button ActionButton() => new Button
    {
        Width = 116,
        Height = 30,
        FlatStyle = FlatStyle.Flat,
        BackColor = Palette.Panel,
        ForeColor = Palette.Ink,
        Margin = new Padding(0, 0, 10, 0),
        FlatAppearance =
        {
            BorderColor = Palette.Border,
            MouseOverBackColor = Palette.AccentWash,
            MouseDownBackColor = Palette.AccentWash,
        },
    };
}
