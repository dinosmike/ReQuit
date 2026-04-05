using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReQuit.Models;

namespace ReQuit.Services;

public static class SettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SettingsFileName = "ReQuit_settings.json";

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReQuit");

    public static string SettingsPath => Path.Combine(SettingsDirectory, SettingsFileName);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (s != null)
                {
                    if (s.Meta.SettingsFormatVersion < 1)
                        s.Meta.SettingsFormatVersion = 1;
                    if (s.Window == null)
                        s.Window = new WindowSettings();
                    if (s.Timer == null)
                        s.Timer = new TimerSettings();
                    return s;
                }
            }
        }
        catch
        {
            // ignored
        }

        return AppSettings.CreateDefault();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignored
        }
    }
}
