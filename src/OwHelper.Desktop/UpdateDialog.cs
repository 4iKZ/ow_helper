using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OwHelper.Desktop;

public sealed class UpdateDialog : Form
{
    readonly TrayController controller;
    readonly UpdateInfo info;
    readonly string currentVersion;

    readonly ProgressBar progressBar = new ProgressBar();
    readonly Label statusLabel = new Label();
    readonly RoundedButton btnUpdate = new RoundedButton();
    readonly RoundedButton btnBrowser = new RoundedButton();
    readonly RoundedButton btnCancel = new RoundedButton();

    CancellationTokenSource? cts;
    bool isDownloading;

    public UpdateDialog(TrayController controller, UpdateInfo info, string currentVersion)
    {
        this.controller = controller;
        this.info = info;
        this.currentVersion = currentVersion;

        Text = "发现新版本 - OW 助手";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(540, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = true;

        try
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe)) Icon = Icon.ExtractAssociatedIcon(exe) ?? Icon;
        }
        catch { }

        BuildLayout();
    }

    void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Palette.Paper,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Release Notes Card
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Progress
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12f)); // Spacer
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Buttons

        // 1. Header
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0, 0, 0, 12),
        };

        var titleLabel = new Label
        {
            Text = "✨ OW 助手有新版本可用！",
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        var versionLabel = new Label
        {
            Text = $"当前版本：v{currentVersion}    ➜    最新版本：v{info.Version}",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Palette.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        string pubStr = info.PublishedAt.HasValue
            ? info.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "未知";

        var metaLabel = new Label
        {
            Text = $"发布日期：{pubStr} · 文件大小：{FormatSize(info.SetupSizeBytes)}",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        header.Controls.Add(titleLabel);
        header.Controls.Add(versionLabel);
        header.Controls.Add(metaLabel);

        // 2. Release Notes Card
        var notesCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.SurfaceSubtle,
            Padding = new Padding(12),
            Margin = new Padding(0),
        };
        notesCard.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Palette.Border);
            DrawRoundedRectangle(e.Graphics, pen, new Rectangle(0, 0, notesCard.Width - 1, notesCard.Height - 1), 6);
        };

        var notesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.SurfaceSubtle,
        };
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var notesHeader = new Label
        {
            Text = "更新日志：",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = Palette.Ink,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6),
        };

        var notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.SurfaceSubtle,
            ForeColor = Palette.Ink,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9f),
            Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "该版本暂无详细更新日志。" : info.ReleaseNotes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };

        notesLayout.Controls.Add(notesHeader, 0, 0);
        notesLayout.Controls.Add(notesBox, 0, 1);
        notesCard.Controls.Add(notesLayout);

        // 3. Progress area
        var progressPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0),
        };

        progressBar.Height = 10;
        progressBar.Width = 492;
        progressBar.Visible = false;
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Margin = new Padding(0, 0, 0, 4);

        statusLabel.AutoSize = true;
        statusLabel.Font = new Font("Microsoft YaHei UI", 8.5f);
        statusLabel.ForeColor = Palette.InkSecondary;
        statusLabel.Text = "";
        statusLabel.Visible = false;
        statusLabel.Margin = new Padding(0);

        progressPanel.Controls.Add(progressBar);
        progressPanel.Controls.Add(statusLabel);

        // 4. Buttons area
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Palette.Paper,
            Margin = new Padding(0),
        };

        btnUpdate.Text = "立即更新并重启";
        btnUpdate.Size = new Size(130, 36);
        btnUpdate.CornerRadius = 6;
        btnUpdate.BorderSize = 0;
        btnUpdate.BackColor = Palette.Accent;
        btnUpdate.ForeColor = Color.White;
        btnUpdate.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        btnUpdate.HoverBackColor = Palette.AccentHover;
        btnUpdate.PressedBackColor = Palette.AccentHover;
        btnUpdate.Margin = new Padding(8, 0, 0, 0);
        btnUpdate.Click += async (s, e) => await StartUpdateAsync();

        btnBrowser.Text = "浏览器下载";
        StyleSecondaryButton(btnBrowser, 100);
        btnBrowser.Click += (s, e) => OpenBrowser(info.HtmlUrl);

        btnCancel.Text = "稍后再说";
        StyleSecondaryButton(btnCancel, 90);
        btnCancel.Click += (s, e) =>
        {
            if (isDownloading)
            {
                cts?.Cancel();
            }
            else
            {
                Close();
            }
        };

        btnPanel.Controls.Add(btnUpdate);
        btnPanel.Controls.Add(btnBrowser);
        btnPanel.Controls.Add(btnCancel);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(notesCard, 0, 1);
        root.Controls.Add(progressPanel, 0, 2);
        root.Controls.Add(btnPanel, 0, 4);

        Controls.Add(root);
    }

    async Task StartUpdateAsync()
    {
        if (isDownloading) return;

        // 1. 挂机运行防护
        if (controller.Session.State == SessionState.Running)
        {
            DialogResult res = MessageBox.Show(this,
                "当前正在后台挂机中，更新将先安全停止挂机并重启程序。\n是否继续更新？",
                "挂机运行中提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes) return;

            statusLabel.Visible = true;
            statusLabel.Text = "正在安全停止挂机并还原窗口...";
            await controller.StopAsync();
        }

        // 2. 便携绿色版路径检测
        if (!UpdateService.IsStandardInstalledPath())
        {
            DialogResult res = MessageBox.Show(this,
                $"检测到当前程序运行于非标准安装目录：\n{Environment.ProcessPath}\n\n" +
                "【是(Yes)】：通过安装包升级并迁移至标准应用目录（推荐，享有桌面快捷方式与后续无缝自更）；\n" +
                "【否(No)】：打开浏览器前往 GitHub Releases 下载便携绿色版 Zip 包；\n" +
                "【取消(Cancel)】：取消本次更新。",
                "便携版更新提示",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information);

            if (res == DialogResult.Cancel) return;
            if (res == DialogResult.No)
            {
                string zipUrl = string.IsNullOrWhiteSpace(info.ZipDownloadUrl) ? info.HtmlUrl : info.ZipDownloadUrl;
                OpenBrowser(zipUrl);
                return;
            }
        }

        // 3. 开始下载流程
        isDownloading = true;
        btnUpdate.Enabled = false;
        btnBrowser.Enabled = false;
        btnCancel.Text = "取消下载";
        progressBar.Visible = true;
        progressBar.Value = 0;
        statusLabel.Visible = true;
        statusLabel.Text = "正在连接下载节点（双通道智能加速）...";

        cts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgressReport>(report =>
        {
            progressBar.Value = Math.Clamp((int)report.Percent, 0, 100);
            statusLabel.Text = $"正在下载：{report.Percent:F1}% ({FormatSize(report.BytesReceived)} / {FormatSize(report.TotalBytes)})";
        });

        string tempDir = Path.Combine(Path.GetTempPath(), "OwHelper_Update");
        string destPath = Path.Combine(tempDir, $"OwHelper-Setup-{info.Version}.exe");

        try
        {
            await controller.UpdateService.DownloadUpdateAsync(info, destPath, progress, cts.Token);
            progressBar.Value = 100;
            statusLabel.Text = "下载完成！即将退出当前程序并静默升级与重启...";
            await Task.Delay(1000);
            UpdateService.ExecuteInstallerAndExit(destPath, silent: true);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "下载已取消。";
            ResetButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"更新下载失败: {ex.Message}\n您可以尝试点击「浏览器下载」手动获取安装包。",
                "下载失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            statusLabel.Text = "下载失败，请尝试使用「浏览器下载」。";
            ResetButtons();
        }
    }

    void ResetButtons()
    {
        isDownloading = false;
        btnUpdate.Enabled = true;
        btnBrowser.Enabled = true;
        btnCancel.Text = "稍后再说";
        cts?.Dispose();
        cts = null;
    }

    void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法打开浏览器: {ex.Message}\n请手动访问: {url}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知大小";
        if (bytes >= 1024 * 1024)
        {
            return $"{(double)bytes / (1024 * 1024):F1} MB";
        }
        return $"{(double)bytes / 1024:F1} KB";
    }

    static void StyleSecondaryButton(RoundedButton button, int width)
    {
        button.Size = new Size(width, 36);
        button.Margin = new Padding(8, 0, 0, 0);
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
}
