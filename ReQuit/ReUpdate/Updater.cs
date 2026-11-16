using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ReUpdate;

/// <summary>
/// Тихое автообновление из релизов GitHub. При запуске программы в фоне проверяет последний релиз;
/// если он новее — скачивает exe и подменяет файл на диске. Запущенная программа работает дальше как была,
/// новая версия запустится в следующий раз. Хитрость: запущенный exe нельзя перезаписать, но можно переименовать.
/// </summary>
public static class Updater
{
    /// <summary>Путь к своему exe, только если это опубликованная одним файлом сборка (в отладке — null).</summary>
    private static string? ExePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            // Рядом с dll — это обычная сборка из bin, а не single-file.
            return File.Exists(Path.ChangeExtension(path, ".dll")) ? null : path;
        }
    }

    /// <summary>Убрать exe прошлой версии, оставшийся после подмены. Вызывать при старте.</summary>
    public static void CleanupAfterUpdate()
    {
        var exe = ExePath;
        if (exe == null)
            return;
        try { File.Delete(exe + ".old"); } catch { /* ещё занят старой копией — удалим в следующий раз */ }
        try { File.Delete(exe + ".new"); } catch { /* ignore */ }
    }

    /// <summary>Один раз при запуске: проверить и, если есть, подготовить новую версию к следующему запуску.</summary>
    public static void Start()
    {
        if (ExePath == null || !ReUpdateConfig.AutoUpdateEnabled())
            return;
        _ = CheckAndDownloadAsync();
    }

    private static async Task CheckAndDownloadAsync()
    {
        var exe = ExePath!;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(ReUpdateConfig.Repo + "-Updater");

            var api = $"https://api.github.com/repos/{ReUpdateConfig.Owner}/{ReUpdateConfig.Repo}/releases/latest";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(api));
            var latest = VersionText.Parse(doc.RootElement.GetProperty("tag_name").GetString() ?? "");
            if (latest == null || latest <= ReUpdateConfig.CurrentVersion)
                return;

            // Сначала отдельный файл для обновлений (его счётчик скачиваний = число обновлений), иначе обычный exe.
            string? url = null, fallbackUrl = null;
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, ReUpdateConfig.UpdateAssetName, StringComparison.OrdinalIgnoreCase))
                    url = asset.GetProperty("browser_download_url").GetString();
                else if (string.Equals(name, ReUpdateConfig.AssetName, StringComparison.OrdinalIgnoreCase))
                    fallbackUrl = asset.GetProperty("browser_download_url").GetString();
            }
            url ??= fallbackUrl;
            if (url == null)
                return;

            var newPath = exe + ".new";
            await using (var src = await http.GetStreamAsync(url))
            await using (var dst = File.Create(newPath))
                await src.CopyToAsync(dst);
            if (!LooksLikeExe(newPath))
            {
                File.Delete(newPath);
                return;
            }

            // Подмена: запущенный exe → .old (программа продолжает работать из него), новый → на его место.
            var oldPath = exe + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(exe, oldPath);
            try
            {
                File.Move(newPath, exe);
            }
            catch
            {
                File.Move(oldPath, exe);
                throw;
            }
        }
        catch
        {
            // Нет сети, лимит GitHub, папка только для чтения — тихо попробуем при следующем запуске.
        }
    }

    private static bool LooksLikeExe(string path)
    {
        var info = new FileInfo(path);
        if (info.Length < 1_000_000)
            return false;
        using var f = File.OpenRead(path);
        return f.ReadByte() == 'M' && f.ReadByte() == 'Z';
    }
}

internal static class VersionText
{
    /// <summary>«ReFlow_1_2», «v1.2», «1.2.3» → версия.</summary>
    public static Version? Parse(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)[._](\d+)(?:[._](\d+))?");
        if (!m.Success)
            return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }
}
