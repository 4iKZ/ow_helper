using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace OwHelper;

internal enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

internal sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Event,
    int? Pid = null,
    nint? Hwnd = null,
    int? PulseIndex = null,
    int? NativeError = null,
    string? Operation = null,
    long? ElapsedMs = null,
    string? Message = null);

internal sealed class AppLog
{
    readonly object gate = new object();
    readonly string filePath;
    readonly LogLevel minLevel;
    int writeFailures;

    public AppLog(string filePath, LogLevel minLevel = LogLevel.Information)
    {
        this.filePath = filePath;
        this.minLevel = minLevel;
    }

    public string FilePath => filePath;

    public int WriteFailures => writeFailures;

    public static string DefaultFilePath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OwHelper",
            "logs");
        return Path.Combine(dir, $"owhelper-{DateTime.Now:yyyyMMdd}.log");
    }

    public static int DeleteOlderThan(string directory, int retainDays)
    {
        int deleted = 0;
        try
        {
            if (!Directory.Exists(directory)) return 0;
            DateTime cutoff = DateTime.Now.AddDays(-retainDays);
            foreach (string file in Directory.EnumerateFiles(directory, "owhelper-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
        }
        catch { }
        return deleted;
    }

    public void Write(LogEntry entry)
    {
        if (entry.Level < minLevel) return;
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            lock (gate)
            {
                File.AppendAllText(filePath, Format(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            Interlocked.Increment(ref writeFailures);
        }
    }

    public IReadOnlyList<string> Tail(int count)
    {
        try
        {
            lock (gate)
            {
                if (!File.Exists(filePath)) return Array.Empty<string>();
                string[] lines = File.ReadAllLines(filePath);
                int start = Math.Max(0, lines.Length - count);
                var result = new List<string>(count);
                for (int i = start; i < lines.Length; i++) result.Add(lines[i]);
                return result;
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    static string Format(LogEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append('|').Append(e.Level.ToString().ToUpperInvariant());
        sb.Append('|').Append(e.Event);
        if (e.Pid is int pid) sb.Append("|pid=").Append(pid);
        if (e.Hwnd is nint hwnd) sb.Append("|hwnd=0x").Append(hwnd.ToInt64().ToString("X8"));
        if (e.PulseIndex is int pulse) sb.Append("|pulse=").Append(pulse);
        if (e.NativeError is int error) sb.Append("|nativeError=").Append(error);
        if (e.Operation != null) sb.Append("|op=").Append(e.Operation);
        if (e.ElapsedMs is long ms) sb.Append("|elapsedMs=").Append(ms);
        if (e.Message != null) sb.Append("|msg=").Append(e.Message.ReplaceLineEndings(" "));
        return sb.ToString();
    }
}
