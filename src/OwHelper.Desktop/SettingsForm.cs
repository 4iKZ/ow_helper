using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OwHelper.Desktop;

public sealed class SettingsForm : Form
{
    internal sealed class KeyItem
    {
        public string Name { get; }
        public string Label { get; }
        public KeyItem(string name, string label) { Name = name; Label = label; }
        public override string ToString() => Label;
    }

    internal sealed class ComboItem
    {
        public string Label { get; }
        public string Value { get; }
        public ComboItem(string label, string value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }

    internal static void Populate(CheckedListBox list)
    {
        foreach ((string name, string label) in SettingsMapper.Presets)
        {
            list.Items.Add(new KeyItem(name, label));
        }
    }

    internal static void ApplySelection(CheckedListBox list, IEnumerable<string> selectedKeys)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            var item = (KeyItem)list.Items[i];
            list.SetItemChecked(i, selectedKeys.Any(k => string.Equals(k, item.Name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    readonly TrayController controller;
    readonly AppConfig config;
    readonly CheckedListBox keyList = new CheckedListBox
    {
        MultiColumn = true,
        ColumnWidth = 165,
        Height = 160,
        IntegralHeight = false,
        CheckOnClick = true,
        BackColor = Palette.SurfaceSubtle,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Top,
        Margin = new Padding(0, 0, 0, 8),
    };
    readonly TextBox customKeys = new TextBox
    {
        Dock = DockStyle.Top,
        Height = 28,
        BackColor = Palette.SurfaceSubtle,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 2, 0, 0),
    };
    readonly NumericUpDown interval = Numeric(5, 300);
    readonly NumericUpDown hold = Numeric(10, 2000);
    readonly NumericUpDown focusWait = Numeric(0, 1000);
    readonly NumericUpDown jitter = Numeric(0, 50);
    readonly ComboBox priority = Combo(new object[]
    {
        new ComboItem("后台优先（推荐）", "BelowNormal"),
        new ComboItem("普通", "Normal"),
        new ComboItem("略高于普通", "AboveNormal"),
        new ComboItem("最低（省电优先）", "Idle"),
        new ComboItem("高", "High"),
    });
    readonly CheckBox ecoQos = Check("后台省电模式（部分电脑不支持，会自动跳过）");
    readonly CheckBox skipForeground = Check("我在玩游戏时自动暂停");
    readonly CheckBox allowOffscreen = Check("启用老板键（隐藏窗口并移出任务栏）");
    readonly CheckBox keepOffscreen = Check("游戏重新打开后保持老板键状态");
    readonly ComboBox logLevel = Combo(new object[]
    {
        new ComboItem("普通（推荐）", "Information"),
        new ComboItem("详细（排错用）", "Debug"),
        new ComboItem("只记问题", "Warning"),
        new ComboItem("只记错误", "Error"),
    });
    readonly NumericUpDown retainDays = Numeric(1, 365);
    readonly CheckBox autoCheckUpdate = Check("启动时自动检查新版本（推荐）");

    public SettingsForm(TrayController controller)
    {
        this.controller = controller;
        config = controller.Config.Clone();

        Text = "OW 助手 · 更多设置";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(620, 720);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe)) Icon = Icon.ExtractAssociatedIcon(exe) ?? Icon;
        }
        catch { }

        Populate(keyList);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 10),
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true,
            BackColor = Palette.Paper,
        };
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 卡片 1：按键设定
        root.Controls.Add(BuildKeyCard(), 0, 0);

        // 卡片 2：节奏设定
        root.Controls.Add(BuildRhythmCard(), 0, 1);

        // 卡片 3：后台与行为
        root.Controls.Add(BuildBehaviorCard(), 0, 2);

        // 卡片 4：运行记录
        root.Controls.Add(BuildLoggingCard(), 0, 3);

        // 卡片 5：版本与更新
        root.Controls.Add(BuildUpdateCard(), 0, 4);

        // 底部提示
        root.Controls.Add(new Label
        {
            Text = "✓ 设置保存后立即生效；正在挂机时，下一次自动按键就会采用新设置。",
            ForeColor = Palette.InkSecondary,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            AutoSize = true,
            Margin = new Padding(4, 4, 0, 8),
        }, 0, 5);

        // 底部按钮栏
        var save = ActionButton("保存并生效", primary: true);
        save.Click += async (s, e) => await SaveAsync();

        var cancel = ActionButton("取消", primary: false);
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        var restore = ActionButton("恢复推荐设置", primary: false);
        restore.Click += (s, e) => LoadFromConfig(QuickPresets.ToConfig(QuickPresets.TorbjornPass));
        new ToolTip().SetToolTip(restore, QuickPresets.TorbjornPass.Title + "：" + QuickPresets.TorbjornPass.Subtitle);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(18, 8, 18, 14),
            Height = 56,
            BackColor = Palette.Paper,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(restore);

        Controls.Add(root);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        LoadFromConfig(config);
    }

    Control BuildKeyCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(keyList, 0, 0);

        var customLabel = new Label
        {
            Text = "自定义按键（英文逗号隔开，例如 mouseleft、f1、up）：",
            ForeColor = Palette.InkSecondary,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        };
        content.Controls.Add(customLabel, 0, 1);
        content.Controls.Add(customKeys, 0, 2);

        return CreateGroupCard("自动按哪些键（可多选）", content);
    }

    Control BuildRhythmCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        for (int i = 0; i < 4; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(Pair("每隔多少秒按一次", interval), 0, 0);
        content.Controls.Add(Pair("每次按住多久（毫秒）", hold), 0, 1);
        content.Controls.Add(Pair("准备等待（毫秒，一般不用改）", focusWait), 0, 2);
        content.Controls.Add(Pair("间隔随机浮动（%，一般不用改）", jitter), 0, 3);

        return CreateGroupCard("按键节奏与延时", content);
    }

    Control BuildBehaviorCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        for (int i = 0; i < 5; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(Pair("后台优先级别", priority), 0, 0);
        content.Controls.Add(ecoQos, 0, 1);
        content.Controls.Add(skipForeground, 0, 2);
        content.Controls.Add(allowOffscreen, 0, 3);
        content.Controls.Add(keepOffscreen, 0, 4);

        return CreateGroupCard("后台运行与行为模式", content);
    }

    Control BuildLoggingCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        for (int i = 0; i < 2; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(Pair("记录详细程度", logLevel), 0, 0);
        content.Controls.Add(Pair("日志保留天数", retainDays), 0, 1);

        return CreateGroupCard("运行记录与日志", content);
    }

    Control BuildUpdateCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        for (int i = 0; i < 3; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        string currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var versionLabel = new Label
        {
            Text = $"当前版本：v{currentVer}",
            ForeColor = Palette.InkSecondary,
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 6),
        };

        var checkBtn = ActionButton("立即检查更新…", primary: false);
        checkBtn.Click += async (s, e) =>
        {
            checkBtn.Enabled = false;
            checkBtn.Text = "正在检查…";
            try
            {
                UpdateCheckResult result = await controller.UpdateService.CheckForUpdatesAsync(currentVer, force: true);
                if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update != null)
                {
                    using var dlg = new UpdateDialog(controller, result.Update, currentVer);
                    dlg.ShowDialog(this);
                }
                else if (result.Status == UpdateCheckStatus.UpdateAvailableButManualOnly && result.Update != null)
                {
                    var choice = MessageBox.Show(this, result.Message, "检查更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                    if (choice == DialogResult.OK)
                    {
                        try { Process.Start(new ProcessStartInfo(result.Update.HtmlUrl) { UseShellExecute = true }); } catch { }
                    }
                }
                else if (result.Status == UpdateCheckStatus.UpToDate)
                {
                    MessageBox.Show(this, result.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, result.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch
            {
                MessageBox.Show(this, "检查更新失败：无法连接更新服务器。\n当前版本状态未知，请稍后重试。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                checkBtn.Enabled = true;
                checkBtn.Text = "立即检查更新…";
            }
        };

        content.Controls.Add(versionLabel, 0, 0);
        content.Controls.Add(autoCheckUpdate, 0, 1);
        content.Controls.Add(checkBtn, 0, 2);

        return CreateGroupCard("版本与更新", content);
    }

    void LoadFromConfig(AppConfig source)
    {
        ApplySelection(keyList, source.Input.Keys);
        customKeys.Text = SettingsMapper.CustomKeysText(source.Input.Keys);
        interval.Value = Clamp(source.Input.IntervalSeconds, interval);
        hold.Value = Clamp(source.Input.HoldMilliseconds, hold);
        focusWait.Value = Clamp(source.Input.FocusWaitMilliseconds, focusWait);
        jitter.Value = Clamp(source.Input.JitterPercent, jitter);
        SelectCombo(priority, source.Resource.Priority, "BelowNormal");
        ecoQos.Checked = source.Resource.EcoQoS;
        skipForeground.Checked = source.Input.SkipWhenTargetForeground;
        allowOffscreen.Checked = source.Window.AllowMoveOffscreen;
        keepOffscreen.Checked = source.Window.KeepOffscreenAcrossRestart;
        SelectCombo(logLevel, source.Logging.Level, "Information");
        retainDays.Value = Clamp(source.Logging.RetainDays, retainDays);
        autoCheckUpdate.Checked = source.Update.AutoCheckOnStartup;
    }

    async Task SaveAsync()
    {
        List<string> selectedPresets = keyList.CheckedItems.Cast<KeyItem>().Select(k => k.Name).ToList();
        List<string> keys = SettingsMapper.MergeKeys(selectedPresets, customKeys.Text);
        if (keys.Count == 0)
        {
            MessageBox.Show(this, "至少选择一个按键。", "OW 助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        config.Input.Keys = keys;
        config.Input.IntervalSeconds = (int)interval.Value;
        config.Input.HoldMilliseconds = (int)hold.Value;
        config.Input.FocusWaitMilliseconds = (int)focusWait.Value;
        config.Input.JitterPercent = (int)jitter.Value;
        config.Input.SkipWhenTargetForeground = skipForeground.Checked;
        config.Resource.Priority = (priority.SelectedItem as ComboItem)?.Value ?? "BelowNormal";
        config.Resource.EcoQoS = ecoQos.Checked;
        config.Window.AllowMoveOffscreen = allowOffscreen.Checked;
        config.Window.KeepOffscreenAcrossRestart = keepOffscreen.Checked;
        config.Logging.Level = (logLevel.SelectedItem as ComboItem)?.Value ?? "Information";
        config.Logging.RetainDays = (int)retainDays.Value;
        config.Update.AutoCheckOnStartup = autoCheckUpdate.Checked;

        List<string> problems = config.Validate();
        string summary;
        try
        {
            summary = await controller.ApplySettingsAsync(config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败，设置未生效：" + ex.Message, "OW 助手 · 更多设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string message = problems.Count == 0
            ? summary
            : string.Join(Environment.NewLine, problems) + Environment.NewLine + Environment.NewLine + summary;
        MessageBox.Show(this, message, "OW 助手 · 更多设置", MessageBoxButtons.OK, MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }

    static Panel CreateGroupCard(string title, Control content)
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = Palette.Panel,
            Padding = new Padding(16, 12, 16, 14),
            Margin = new Padding(0, 0, 0, 12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        card.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Palette.Border);
            DrawRoundedRectangle(e.Graphics, pen, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Palette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Palette.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        content.Dock = DockStyle.Top;
        content.Margin = new Padding(0);

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(content, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    static decimal Clamp(int value, NumericUpDown control)
        => Math.Max(control.Minimum, Math.Min(control.Maximum, value));

    static NumericUpDown Numeric(int min, int max) => new NumericUpDown
    {
        Minimum = min,
        Maximum = max,
        Width = 120,
        Height = 28,
        BackColor = Palette.SurfaceSubtle,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
    };

    static void SelectCombo(ComboBox combo, string value, string fallback)
    {
        object? match = FindComboItem(combo, value) ?? FindComboItem(combo, fallback);
        if (match != null) combo.SelectedItem = match;
    }

    static object? FindComboItem(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
        {
            if (item is ComboItem comboItem && string.Equals(comboItem.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return null;
    }

    static ComboBox Combo(object[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Palette.SurfaceSubtle,
            ForeColor = Palette.Ink,
        };
        combo.Items.AddRange(items);
        return combo;
    }

    static CheckBox Check(string text) => new CheckBox
    {
        Text = text,
        ForeColor = Palette.Ink,
        Font = new Font("Microsoft YaHei UI", 9f),
        AutoSize = true,
        Dock = DockStyle.Top,
        Margin = new Padding(0, 4, 0, 4),
    };

    static RoundedButton ActionButton(string text, bool primary) => new RoundedButton
    {
        Text = text,
        CornerRadius = 6,
        BorderSize = primary ? 0 : 1,
        BorderColor = Palette.Border,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(110, 36),
        Padding = new Padding(16, 0, 16, 0),
        BackColor = primary ? Palette.Accent : Palette.Panel,
        ForeColor = primary ? Color.White : Palette.Ink,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        Margin = new Padding(0, 0, 10, 0),
        HoverBackColor = primary ? Palette.AccentHover : Palette.SurfaceSubtle,
        PressedBackColor = primary ? Palette.AccentHover : Palette.AccentWash,
    };

    static TableLayoutPanel Pair(string caption, Control control)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 3, 0, 3),
            Padding = new Padding(0),
            BackColor = Palette.Panel,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = caption,
            ForeColor = Palette.InkSecondary,
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4),
        };

        control.Dock = DockStyle.Left;
        control.Margin = new Padding(0, 3, 0, 3);

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
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
