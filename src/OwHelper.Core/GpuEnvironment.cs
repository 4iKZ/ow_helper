using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace OwHelper.Core;

public sealed record GpuInfo(string Vendor, string Name);

public static class GpuEnvironment
{
    const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static IReadOnlyList<GpuInfo> Detect()
    {
        var list = new List<GpuInfo>();
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (root == null) return list;
            foreach (string subKeyName in root.GetSubKeyNames())
            {
                using RegistryKey? key = root.OpenSubKey(subKeyName);
                if (key == null) continue;
                string? name = key.GetValue("DriverDesc") as string;
                string? vendor = key.GetValue("ProviderName") as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new GpuInfo(vendor ?? "", name));
                }
            }
        }
        catch
        {
        }
        return list;
    }

    public static bool HasNvidia(IReadOnlyList<GpuInfo> gpus)
        => gpus.Any(g =>
            g.Vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
}
