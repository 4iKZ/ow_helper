using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OwHelper.Core;
using Xunit;

namespace OwHelper.Core.Tests;

public class ArchitectureTests
{
    static readonly string[] ForbiddenApis =
    {
        "WriteProcessMemory",
        "ReadProcessMemory",
        "VirtualAllocEx",
        "CreateRemoteThread",
        "SetWindowsHookEx",
        "NtWriteVirtualMemory",
        "ZwWriteVirtualMemory",
    };

    [Theory]
    [InlineData("BgKeyProbe")]
    [InlineData("OwHelper")]
    public void AppAssembly_DeclaresNoPInvokes(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);
        var offenders = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null)
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Core_DoesNotReferenceUiFrameworks()
    {
        var references = typeof(ResourceGovernor).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        Assert.DoesNotContain(references, name => name.Contains("Windows.Forms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceTree_ContainsNoForbiddenApis()
    {
        DirectoryInfo root = FindRepoRoot();
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (string api in ForbiddenApis)
            {
                if (text.Contains(api, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file)}: {api}");
                }
            }
        }
        Assert.Empty(offenders);
    }

    static DirectoryInfo FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ow_helper.sln"))) return current;
            current = current.Parent;
        }
        throw new InvalidOperationException("找不到仓库根目录（ow_helper.sln）");
    }
}
