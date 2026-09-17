using System;
using System.IO;
using System.Text.Json;

namespace OwHelper;

public sealed record RuntimeState(
    int Version,
    int Pid,
    DateTime? ProcessStartTimeUtc,
    long Hwnd,
    int Left,
    int Top,
    bool OffscreenApplied);

public sealed class RuntimeStateStore
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    readonly string path;

    public RuntimeStateStore(string path) => this.path = path;

    public string Path => path;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwHelper",
        "runtime-state.json");

    public void Save(RuntimeState state)
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    public RuntimeState? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<RuntimeState>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}

