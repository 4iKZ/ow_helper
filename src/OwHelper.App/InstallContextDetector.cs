using System;
using System.IO;

namespace OwHelper;

public enum InstallMode
{
    Installed,
    Portable
}

public sealed record InstallContext(
    InstallMode Mode,
    string CurrentExecutablePath,
    string CurrentDirectory);

public static class InstallContextDetector
{
    public const string MarkerFileName = "owhelper.install.marker";
    public const string LegacyUninstallerFileName = "unins000.exe";

    public static InstallContext Detect(string? currentExecutablePath = null)
    {
        string exePath = currentExecutablePath ?? Environment.ProcessPath ?? "";
        string dir;

        if (!string.IsNullOrWhiteSpace(exePath))
        {
            string? parent = Path.GetDirectoryName(exePath);
            dir = string.IsNullOrWhiteSpace(parent) ? AppContext.BaseDirectory : parent;
        }
        else
        {
            dir = AppContext.BaseDirectory;
        }

        dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string markerPath = Path.Combine(dir, MarkerFileName);
        string uninstallerPath = Path.Combine(dir, LegacyUninstallerFileName);

        InstallMode mode = (File.Exists(markerPath) || File.Exists(uninstallerPath))
            ? InstallMode.Installed
            : InstallMode.Portable;

        return new InstallContext(mode, exePath, dir);
    }
}
