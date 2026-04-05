using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace ReQuit.Services;

/// <summary>Строки из JSON (EmbeddedResource Localization/&lt;код&gt;.json). В XAML: DynamicResource Loc_&lt;ключ с точками как _&gt;.</summary>
public static class LocalizationService
{
    private const string ResourcePrefix = "ReQuit.Localization.";

    private static readonly JsonSerializerOptions JsonOptions = new() { ReadCommentHandling = JsonCommentHandling.Skip };

    private static ResourceDictionary? _locDictionary;
    private static IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
    private static string _currentCode = "en";

    public static event Action? LanguageChanged;

    public static string CurrentLanguageCode => _currentCode;

    public static string T(string key)
    {
        if (_strings.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s))
            return s;

        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var fmt = T(key);
        try
        {
            return args.Length == 0 ? fmt : string.Format(CultureInfo.CurrentCulture, fmt, args);
        }
        catch (FormatException)
        {
            return fmt;
        }
    }

    public static void Apply(string languageCode)
    {
        var code = AppLanguageCatalog.Normalize(languageCode);
        if (code == _currentCode && _locDictionary != null)
        {
            TrySetUiCulture(code);
            return;
        }

        _currentCode = code;

        var merged = LoadMerged(code);
        _strings = merged;

        var app = Application.Current;
        if (app == null)
            return;

        ApplyResourceDictionary(app, merged);
        TrySetUiCulture(code);
        LanguageChanged?.Invoke();
    }

    private static void TrySetUiCulture(string code)
    {
        try
        {
            var cultureName = MapToBclCultureName(code);
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // ignored
        }
    }

    private static string MapToBclCultureName(string appLanguageCode) => appLanguageCode;

    private static Dictionary<string, string> LoadMerged(string primaryCode)
    {
        var primary = LoadLanguageFile(primaryCode);
        var en = primaryCode.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? primary
            : LoadLanguageFile("en");
        var ru = primaryCode.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? primary
            : LoadLanguageFile("ru");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ru)
            result[kv.Key] = kv.Value;
        foreach (var kv in en)
            result[kv.Key] = kv.Value;
        foreach (var kv in primary)
            result[kv.Key] = kv.Value;

        return result;
    }

    private static Dictionary<string, string> LoadLanguageFile(string code)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = ResourcePrefix + code + ".json";
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        return dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void ApplyResourceDictionary(Application app, IReadOnlyDictionary<string, string> strings)
    {
        var merged = app.Resources.MergedDictionaries;
        if (_locDictionary != null)
            merged.Remove(_locDictionary);

        _locDictionary = new ResourceDictionary();
        foreach (var kv in strings)
        {
            if (string.IsNullOrEmpty(kv.Key))
                continue;
            _locDictionary[ToResourceKey(kv.Key)] = kv.Value;
        }

        merged.Add(_locDictionary);
    }

    public static string ToResourceKey(string logicalKey) =>
        "Loc_" + logicalKey.Replace('.', '_');
}
