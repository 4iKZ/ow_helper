using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OwHelper.Core;

namespace OwHelper;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 2;

    public TargetSection Target { get; set; } = new TargetSection();
    public InputSection Input { get; set; } = new InputSection();
    public ResourceSection Resource { get; set; } = new ResourceSection();
    public WindowSection Window { get; set; } = new WindowSection();
    public LoggingSection Logging { get; set; } = new LoggingSection();

    public sealed class TargetSection
    {
        public string ProcessName { get; set; } = "Overwatch";
    }

    public sealed class InputSection
    {
        public List<string> Keys { get; set; } = new List<string> { "shift" };
        public int IntervalSeconds { get; set; } = 30;
        public int HoldMilliseconds { get; set; } = 200;
        public int FocusWaitMilliseconds { get; set; } = 50;
        public bool SendFocus { get; set; } = true;
        public bool SendActivate { get; set; }
        public bool SendActivateApp { get; set; }
        public bool SkipWhenTargetForeground { get; set; } = true;
        public int JitterPercent { get; set; }
    }

    public sealed class ResourceSection
    {
        public string Priority { get; set; } = "BelowNormal";
        public bool EcoQoS { get; set; } = true;
        public int GpuBackgroundFpsTarget { get; set; } = 20;
        public string GpuPolicyMode { get; set; } = "GuideOnly";
    }

    public sealed class WindowSection
    {
        public bool AllowMoveOffscreen { get; set; } = true;
        public bool KeepOffscreenAcrossRestart { get; set; }
    }

    public sealed class LoggingSection
    {
        public string Level { get; set; } = "Information";
        public int RetainDays { get; set; } = 7;
    }

    static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwHelper",
        "config.json");

    public static AppConfig Load(string path, out List<string> problems)
    {
        problems = new List<string>();
        if (!File.Exists(path))
        {
            var fresh = new AppConfig();
            try { fresh.Save(path); }
            catch (Exception ex) { problems.Add($"无法创建默认配置文件: {ex.Message}"); }
            return fresh;
        }

        AppConfig? config = null;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            config = TryLoadBackup(path, problems);
            if (config == null)
            {
                problems.Add($"config.json 解析失败（原文件保持不动，本次使用默认值）: {ex.Message}");
            }
        }
        catch (IOException ex)
        {
            problems.Add($"config.json 读取失败: {ex.Message}");
        }

        config ??= new AppConfig();
        problems.AddRange(config.Validate());
        return config;
    }

    static AppConfig? TryLoadBackup(string path, List<string> problems)
    {
        string backup = path + ".bak";
        try
        {
            if (!File.Exists(backup)) return null;
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(backup), Options);
            if (config == null) return null;
            problems.Add("config.json 已损坏，已从备份恢复（下次保存后自动修复主文件）");
            return config;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); }
            catch { }
            throw;
        }
    }

    public AppConfig Clone()
        => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, Options), Options) ?? new AppConfig();

    public ResourcePolicy BuildPolicy()
    {
        ProcessPriorityClass priority = ProcessPriorityClass.BelowNormal;
        if (Enum.TryParse(Resource.Priority, true, out ProcessPriorityClass parsed)) priority = parsed;
        return new ResourcePolicy(priority, Resource.EcoQoS);
    }

    public PulseRecipe BuildRecipe()
    {
        return new PulseRecipe
        {
            Keys = Input.Keys.Select(KeyNames.Parse).ToArray(),
            SendFocus = Input.SendFocus,
            SendActivate = Input.SendActivate,
            SendActivateApp = Input.SendActivateApp,
            FocusWaitMs = Input.FocusWaitMilliseconds,
            HoldMs = Input.HoldMilliseconds,
        };
    }

    public LogLevel MinLogLevel()
        => Enum.TryParse(Logging.Level, true, out LogLevel level) ? level : LogLevel.Information;

    public bool GpuGuideEnabled
        => !string.Equals(Resource.GpuPolicyMode, "Disabled", StringComparison.OrdinalIgnoreCase);

    public List<string> Validate()
    {
        var problems = new List<string>();
        if (SchemaVersion != 2)
        {
            problems.Add($"config schemaVersion={SchemaVersion} 不是本版本期望的 2，已按当前版本解读");
            SchemaVersion = 2;
        }
        if (Input.IntervalSeconds < 5 || Input.IntervalSeconds > 300)
        {
            int clamped = Math.Clamp(Input.IntervalSeconds, 5, 300);
            problems.Add($"input.intervalSeconds={Input.IntervalSeconds} 超出 5-300，已调整为 {clamped}");
            Input.IntervalSeconds = clamped;
        }
        if (Input.JitterPercent < 0 || Input.JitterPercent > 50)
        {
            int clamped = Math.Clamp(Input.JitterPercent, 0, 50);
            problems.Add($"input.jitterPercent={Input.JitterPercent} 超出 0-50，已调整为 {clamped}");
            Input.JitterPercent = clamped;
        }
        if (Input.HoldMilliseconds < 10 || Input.HoldMilliseconds > 2000)
        {
            int clamped = Math.Clamp(Input.HoldMilliseconds, 10, 2000);
            problems.Add($"input.holdMilliseconds={Input.HoldMilliseconds} 超出 10-2000，已调整为 {clamped}");
            Input.HoldMilliseconds = clamped;
        }
        if (Input.FocusWaitMilliseconds < 0 || Input.FocusWaitMilliseconds > 1000)
        {
            int clamped = Math.Clamp(Input.FocusWaitMilliseconds, 0, 1000);
            problems.Add($"input.focusWaitMilliseconds={Input.FocusWaitMilliseconds} 超出 0-1000，已调整为 {clamped}");
            Input.FocusWaitMilliseconds = clamped;
        }
        if (Input.Keys == null || Input.Keys.Count == 0)
        {
            problems.Add("input.keys 为空，已回退到 [\"shift\"]");
            Input.Keys = new List<string> { "shift" };
        }
        else
        {
            foreach (string key in Input.Keys)
            {
                try
                {
                    KeyNames.Parse(key);
                }
                catch (ArgumentException)
                {
                    problems.Add($"未知按键 '{key}'，已回退到 [\"shift\"]");
                    Input.Keys = new List<string> { "shift" };
                    break;
                }
            }
        }
        if (!Enum.TryParse<ProcessPriorityClass>(Resource.Priority, true, out _))
        {
            problems.Add($"resource.priority='{Resource.Priority}' 无法识别，已回退到 BelowNormal");
            Resource.Priority = "BelowNormal";
        }
        if (Resource.GpuBackgroundFpsTarget < 20 || Resource.GpuBackgroundFpsTarget > 200)
        {
            int clamped = Math.Clamp(Resource.GpuBackgroundFpsTarget, 20, 200);
            problems.Add($"resource.gpuBackgroundFpsTarget={Resource.GpuBackgroundFpsTarget} 超出 20-200，已调整为 {clamped}");
            Resource.GpuBackgroundFpsTarget = clamped;
        }
        if (Logging.RetainDays < 1 || Logging.RetainDays > 365)
        {
            int clamped = Math.Clamp(Logging.RetainDays, 1, 365);
            problems.Add($"logging.retainDays={Logging.RetainDays} 超出 1-365，已调整为 {clamped}");
            Logging.RetainDays = clamped;
        }
        return problems;
    }
}

