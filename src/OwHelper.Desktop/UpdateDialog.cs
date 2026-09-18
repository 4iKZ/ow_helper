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
    readonly Func<VerifiedUpdatePackage, InstallTarget, Task> installVerifiedPackageAsync;

    readonly ProgressBar progressBar = new ProgressBar();
    readonly Label statusLabel = new Label();
    readonly RoundedButton btnUpdate = new RoundedButton();
    readonly RoundedButton btnBrowser = new RoundedButton();
    readonly RoundedButton btnCancel = new RoundedButton();

    CancellationTokenSource? cts;
    bool isDownloading;

    public UpdateDialog(
        TrayController controller,
        UpdateInfo info,
        string currentVersion,
        Func<VerifiedUpdatePackage, InstallTarget, Task>? installVerifiedPackageAsync = null)
    {
        this.controller = controller;
        this.info = info;
        this.currentVersion = currentVersion;
        this.installVerifiedPackageAsync = installVerifiedPackageAsync ?? controller.InstallVerifiedPackageAsync ?? throw new InvalidOperationException("未配置更新安装回调。");

        Text = "发现新版本 - OW 助手";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
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

        // 1. 便携绿色版与安装上下文检测
        InstallContext installContext = InstallContextDetector.Detect();
        InstallTarget target;

        if (installContext.Mode == InstallMode.Portable)
        {
            DialogResult res = MessageBox.Show(this,
                "当前为绿色便携版。\n\n" +
                "【是(Yes)】：安装到标准应用目录并自动更新（推荐，享有桌面快捷方式与后续无缝自更）；\n" +
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

            target = new InstallTarget(
                TargetDirectory: UpdateService.StandardInstallDirectory,
                RestartExecutablePath: UpdateService.StandardExecutablePath);
        }
        else
        {
            target = new InstallTarget(
                TargetDirectory: installContext.CurrentDirectory,
                RestartExecutablePath: installContext.CurrentExecutablePath);
        }

        // 2. 开始下载流程（下载期间保持 Session 继续运行）
        isDownloading = true;
        btnUpdate.Enabled = false;
        btnBrowser.Enabled = false;
        btnCancel.Text = "取消下载";
        progressBar.Visible = true;
        progressBar.Value = 0;
        statusLabel.Visible = true;
        statusLabel.Text = "正在连接更新服务器…";

        cts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgressReport>(report =>
        {
            progressBar.Value = Math.Clamp((int)report.Percent, 0, 100);
            statusLabel.Text = $"正在下载：{report.Percent:F1}% ({FormatSize(report.BytesReceived)} / {FormatSize(report.TotalBytes)})";
        });

        string tempDir = Path.Combine(Path.GetTempPath(), "OwHelper_Update");
        string destPath = Path.Combine(tempDir, $"OwHelper-Setup-{info.Version}.exe");

        VerifiedUpdatePackage package;
        try
        {
            package = await controller.UpdateService.DownloadAndVerifyUpdateAsync(
                info,
                destPath,
                progress,
                cts.Token,
                status =>
                {
                    if (!IsDisposed)
                    {
                        try { Invoke(() => statusLabel.Text = status); } catch { }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "下载已取消。";
            ResetButtons();
            return;
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
            return;
        }

        // 3. 下载与校验完成，只有在安装包完全就绪后，才检查挂机状态并提示退出
        progressBar.Value = 100;
        statusLabel.Text = "更新包已验证";

        if (controller.Session.IsRunning)
        {
            DialogResult res = MessageBox.Show(this,
                "更新安装包已就绪。当前正在后台挂机中，安装更新需要停止挂机并重启程序。\n是否立即停止挂机并安装？",
                "挂机运行中提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes)
            {
                statusLabel.Text = "更新包已就绪。您可以稍后再次点击「立即更新并重启」。";
                ResetButtons();
                return;
            }

            statusLabel.Text = "正在安全停止后台任务…";
            await controller.StopAsync();
        }

        statusLabel.Text = "正在启动安装程序…";
        await installVerifiedPackageAsync(package, target);
        if (!IsDisposed)
        {
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (isDownloading)
        {
            cts?.Cancel();
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
