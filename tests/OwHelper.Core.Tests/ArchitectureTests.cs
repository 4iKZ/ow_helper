using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace OwHelper.Core.Tests;

public class ArchitectureTests
{
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
}
