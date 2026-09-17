using System;
using System.Collections.Generic;
using System.Linq;

namespace OwHelper.Desktop;

public static class SettingsMapper
{
    public static IReadOnlyList<(string Name, string Label)> Presets { get; } = new (string Name, string Label)[]
    {
        ("mouseleft", "鼠标左键 LMB"),
        ("mouseright", "鼠标右键 RMB"),
        ("mousemiddle", "鼠标中键 MMB"),
        ("mouse4", "鼠标侧键 X1"),
        ("mouse5", "鼠标侧键 X2"),
        ("shift", "Shift（技能1）"),
        ("e", "E（技能2）"),
        ("q", "Q（终极）"),
        ("v", "V（近战）"),
        ("f", "F（互动）"),
        ("r", "R（装弹）"),
        ("space", "Space（跳跃）"),
        ("ctrl", "Ctrl（蹲下）"),
        ("w", "W（前进）"),
        ("a", "A（左移）"),
        ("s", "S（后退）"),
        ("d", "D（右移）"),
        ("1", "1（武器1）"),
        ("2", "2（武器2）"),
        ("tab", "Tab（计分板）"),
    };

    public static bool IsPreset(string key)
        => Presets.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

    public static List<string> MergeKeys(IEnumerable<string> checkedPresets, string customText)
    {
        var result = new List<string>();
        foreach (string preset in checkedPresets) Add(result, preset);
        foreach (string part in (customText ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) Add(result, part);
        return result;

        static void Add(List<string> keys, string raw)
        {
            string key = raw.Trim();
            if (key.Length == 0) return;
            if (keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))) return;
            keys.Add(key);
        }
    }

    public static string CustomKeysText(IEnumerable<string> keys)
        => string.Join(",", keys.Where(k => !IsPreset(k)));
}

