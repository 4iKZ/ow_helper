using System;
using System.Collections.Generic;
using System.Drawing;
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
        ColumnWidth = 190,
        Height = 120,
        CheckOnClick = true,
        BackColor = Palette.Panel,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
        Width = 540,
    };
    readonly TextBox customKeys = new TextBox
    {
        Width = 540,
        BackColor = Palette.Panel,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
    };
    readonly NumericUpDown interval = Numeric(5, 300);
    readonly NumericUpDown hold = Numeric(10, 2000);
    readonly NumericUpDown focusWait = Numeric(0, 1000);
    readonly NumericUpDown jitter = Numeric(0, 50);
    readonly ComboBox priority = Combo(new[] { "Normal", "BelowNormal", "AboveNormal", "Idle", "High" });
    readonly CheckBox ecoQos = Check("EcoQoS 节能（部分电源方案不支持，会如实报告）");
    readonly CheckBox skipForeground = Check("OW 在前台时跳过脉冲");
    readonly CheckBox allowOffscreen = Check("允许窗口移出屏幕（m 键可用）");
    readonly CheckBox keepOffscreen = Check("重连后保持移出屏幕");
    readonly ComboBox logLevel = Combo(new[] { "Debug", "Information", "Warning", "Error" });
    readonly NumericUpDown retainDays = Numeric(1, 365);

    public SettingsForm(TrayController controller)
    {
        this.controller = controller;
        config = controller.Config;

        Text = "OW Helper 设置";
        BackColor = Palette.Paper;
        ForeColor = Palette.Ink;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(584, 656);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        Populate(keyList);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 8),
            ColumnCount = 1,
            AutoScroll = true,
        };
        AddSection(layout, "按键（可多选；OW 常用键位，含鼠标）");
        AddRow(layout, keyList);
        AddRow(layout, Field("自定义（逗号分隔，支持 mouse4 / f1 / up / numdivide 等）", customKeys));
        AddSection(layout, "时序");
        AddRow(layout, Pair("间隔（秒）", interval));
        AddRow(layout, Pair("按住（毫秒）", hold));
        AddRow(layout, Pair("focus 等待（毫秒）", focusWait));
        AddRow(layout, Pair("抖动（%）", jitter));
        AddSection(layout, "资源");
        AddRow(layout, Pair("CPU 优先级", priority));
        AddRow(layout, ecoQos);
        AddSection(layout, "窗口与行为");
        AddRow(layout, skipForeground);
        AddRow(layout, allowOffscreen);
        AddRow(layout, keepOffscreen);
        AddSection(layout, "日志");
        AddRow(layout, Pair("级别", logLevel));
        AddRow(layout, Pair("保留天数", retainDays));
        AddRow(layout, new Label
        {
            Text = "保存后：间隔 / 抖动 / 优先级 / 前台跳过 / 窗口选项立即生效；\n按键与按住、focus 时序在下次「开始」生效。",
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        });

        var save = ActionButton("保存");
        save.Click += async (s, e) => await SaveAsync();
        var cancel = ActionButton("取消");
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var restore = ActionButton("恢复默认");
        restore.Click += (s, e) => LoadFromConfig(new AppConfig());
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(20, 0, 20, 14),
            Height = 56,
            BackColor = Palette.Paper,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(restore);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = save;

        LoadFromConfig(config);
    }

    void LoadFromConfig(AppConfig source)
    {
        ApplySelection(keyList, source.Input.Keys);
        customKeys.Text = SettingsMapper.CustomKeysText(source.Input.Keys);
        interval.Value = Clamp(source.Input.IntervalSeconds, interval);
        hold.Value = Clamp(source.Input.HoldMilliseconds, hold);
        focusWait.Value = Clamp(source.Input.FocusWaitMilliseconds, focusWait);
        jitter.Value = Clamp(source.Input.JitterPercent, jitter);
        priority.SelectedItem = priority.Items.Contains(source.Resource.Priority) ? source.Resource.Priority : "BelowNormal";
        ecoQos.Checked = source.Resource.EcoQoS;
        skipForeground.Checked = source.Input.SkipWhenTargetForeground;
        allowOffscreen.Checked = source.Window.AllowMoveOffscreen;
        keepOffscreen.Checked = source.Window.KeepOffscreenAcrossRestart;
        logLevel.SelectedItem = logLevel.Items.Contains(source.Logging.Level) ? source.Logging.Level : "Information";
        retainDays.Value = Clamp(source.Logging.RetainDays, retainDays);
    }

    async Task SaveAsync()
    {
        List<string> selectedPresets = keyList.CheckedItems.Cast<KeyItem>().Select(k => k.Name).ToList();
        List<string> keys = SettingsMapper.MergeKeys(selectedPresets, customKeys.Text);
        if (keys.Count == 0)
        {
            MessageBox.Show(this, "至少选择一个按键。", "OW Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        config.Input.Keys = keys;
        config.Input.IntervalSeconds = (int)interval.Value;
        config.Input.HoldMilliseconds = (int)hold.Value;
        config.Input.FocusWaitMilliseconds = (int)focusWait.Value;
        config.Input.JitterPercent = (int)jitter.Value;
        config.Input.SkipWhenTargetForeground = skipForeground.Checked;
        config.Resource.Priority = priority.SelectedItem?.ToString() ?? "BelowNormal";
        config.Resource.EcoQoS = ecoQos.Checked;
        config.Window.AllowMoveOffscreen = allowOffscreen.Checked;
        config.Window.KeepOffscreenAcrossRestart = keepOffscreen.Checked;
        config.Logging.Level = logLevel.SelectedItem?.ToString() ?? "Information";
        config.Logging.RetainDays = (int)retainDays.Value;

        List<string> problems = config.Validate();
        string summary = await controller.ApplySettingsAsync(config);
        string message = problems.Count == 0
            ? summary
            : string.Join(Environment.NewLine, problems) + Environment.NewLine + Environment.NewLine + summary;
        MessageBox.Show(this, message, "OW Helper 设置", MessageBoxButtons.OK, MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }

    static decimal Clamp(int value, NumericUpDown control)
        => Math.Max(control.Minimum, Math.Min(control.Maximum, value));

    static NumericUpDown Numeric(int min, int max) => new NumericUpDown
    {
        Minimum = min,
        Maximum = max,
        Width = 110,
        BackColor = Palette.Panel,
        ForeColor = Palette.Ink,
        BorderStyle = BorderStyle.FixedSingle,
    };

    static ComboBox Combo(string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 160,
            BackColor = Palette.Panel,
            ForeColor = Palette.Ink,
        };
        combo.Items.AddRange(items);
        return combo;
    }

    static CheckBox Check(string text) => new CheckBox
    {
        Text = text,
        ForeColor = Palette.Ink,
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 4),
    };

    static Button ActionButton(string text) => new Button
    {
        Text = text,
        Width = 108,
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

    static void AddSection(TableLayoutPanel layout, string text)
    {
        layout.Controls.Add(new Label
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Palette.Accent,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 4),
        });
    }

    static void AddRow(TableLayoutPanel layout, Control control)
    {
        control.Margin = new Padding(0, 2, 0, 2);
        layout.Controls.Add(control);
    }

    static Panel Field(string caption, Control control)
    {
        var panel = new Panel { Width = 544, Height = 28 };
        var label = new Label
        {
            Text = caption,
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            Location = new Point(0, 5),
        };
        control.Location = new Point(0, 24);
        panel.Controls.Add(label);
        control.Dock = DockStyle.Bottom;
        panel.Controls.Add(control);
        panel.Height = 52;
        return panel;
    }

    static Panel Pair(string caption, Control control)
    {
        var panel = new Panel { Width = 544, Height = 30 };
        var label = new Label
        {
            Text = caption,
            ForeColor = Palette.InkSecondary,
            AutoSize = true,
            Location = new Point(0, 6),
            Width = 190,
        };
        control.Location = new Point(200, 2);
        panel.Controls.Add(label);
        panel.Controls.Add(control);
        return panel;
    }
}

