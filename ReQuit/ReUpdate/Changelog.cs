using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReUpdate;

/// <summary>Одна версия из changelog.json.</summary>
public sealed class ChangelogVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("ru")] public List<string> Ru { get; set; } = new();
    [JsonPropertyName("en")] public List<string> En { get; set; } = new();

    internal Version? Parsed => VersionText.Parse(Version);
    internal IReadOnlyList<string> Items => ReUpdateConfig.IsRussian || En.Count == 0 ? Ru : En;
}

internal sealed class ChangelogFile
{
    [JsonPropertyName("versions")] public List<ChangelogVersion> Versions { get; set; } = new();
}

/// <summary>История версий — только из changelog.json, вшитого в exe при сборке. Из сети ничего не грузится.</summary>
public static class Changelog
{
    public static List<ChangelogVersion> Load()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            var name = asm?.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("changelog.json", StringComparison.OrdinalIgnoreCase));
            if (asm == null || name == null)
                return new();
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            var file = JsonSerializer.Deserialize<ChangelogFile>(r.ReadToEnd());
            return (file?.Versions ?? new())
                .Where(v => v.Parsed != null && v.Ru.Count + v.En.Count > 0)
                .OrderByDescending(v => v.Parsed)
                .ToList();
        }
        catch
        {
            return new();
        }
    }
}

/// <summary>
/// «Есть непрочитанное "Что нового"»: после обновления версия программы больше, чем последняя
/// увиденная. При первом запуске (новый пользователь) отмечаем текущую как увиденную — точки нет.
/// </summary>
public static class WhatsNewState
{
    private const string StateFile = "reupdate-state.json";

    public static event Action? Changed;

    public static Version? LastSeen { get; private set; }

    public static bool HasUnseen => LastSeen != null && ReUpdateConfig.CurrentVersion > LastSeen;

    public static void Initialize()
    {
        try
        {
            var path = Path.Combine(ReUpdateConfig.DataDir, StateFile);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                LastSeen = VersionText.Parse(doc.RootElement.GetProperty("lastSeen").GetString() ?? "");
            }
        }
        catch { /* битый файл — как первый запуск */ }

        if (LastSeen == null)
            MarkSeen();
    }

    public static void MarkSeen()
    {
        LastSeen = ReUpdateConfig.CurrentVersion;
        try
        {
            File.WriteAllText(Path.Combine(ReUpdateConfig.DataDir, StateFile),
                JsonSerializer.Serialize(new { lastSeen = LastSeen.ToString(3) }));
        }
        catch { /* ignore */ }
        Changed?.Invoke();
    }
}
