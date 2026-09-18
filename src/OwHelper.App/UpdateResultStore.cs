using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OwHelper;

public sealed record UpdateResultRecord(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("completedAt")] DateTimeOffset CompletedAt,
    [property: JsonPropertyName("installerExitCode")] int InstallerExitCode,
    [property: JsonPropertyName("restarted")] bool Restarted);

public static class UpdateResultStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwHelper",
        "update-result.json");

    public static void Save(UpdateResultRecord record, string? path = null)
    {
        string filePath = path ?? DefaultPath;
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        string json = JsonSerializer.Serialize(record, options);
        File.WriteAllText(filePath, json);
    }

    public static UpdateResultRecord? Load(string? path = null)
    {
        string filePath = path ?? DefaultPath;
        try
        {
            if (!File.Exists(filePath)) return null;
            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<UpdateResultRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string? path = null)
    {
        string filePath = path ?? DefaultPath;
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }
}
