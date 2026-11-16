using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ReUpdate;

/// <summary>
/// Скрытая статистика: сколько раз скачали exe с GitHub и сколько программ обновилось
/// (счётчик файла <see cref="ReUpdateConfig.UpdateAssetName"/>). Только для автора, в интерфейсе не подписано.
/// </summary>
public static class DownloadStats
{
    private static string T(string ru, string en) => ReUpdateConfig.IsRussian ? ru : en;

    /// <summary>Счётчики с GitHub готовым текстом (несколько строк). Без сети — текст об ошибке.</summary>
    public static async Task<string> LoadTextAsync()
    {
        try
        {
            return await LoadCoreAsync();
        }
        catch
        {
            return T("Не удалось получить статистику с GitHub.", "Could not get statistics from GitHub.");
        }
    }

    private static async Task<string> LoadCoreAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ReUpdateConfig.Repo + "-Stats");
        var api = $"https://api.github.com/repos/{ReUpdateConfig.Owner}/{ReUpdateConfig.Repo}/releases?per_page=100";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(api));

        var lines = new StringBuilder();
        long totalDownloads = 0, totalUpdates = 0;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            long downloads = 0, updates = 0;
            var found = false;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var count = asset.GetProperty("download_count").GetInt64();
                if (string.Equals(name, ReUpdateConfig.AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloads += count;
                    found = true;
                }
                else if (string.Equals(name, ReUpdateConfig.UpdateAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    updates += count;
                    found = true;
                }
            }
            // Релизы без exe программы (например, файл для замера) не считаем.
            if (!found)
                continue;

            totalDownloads += downloads;
            totalUpdates += updates;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = VersionText.Parse(tag);
            var label = version != null ? $"{version.Major}.{version.Minor}" : tag;
            lines.AppendLine(T($"{label}:  скачали {downloads}, обновились {updates}",
                               $"{label}:  downloaded {downloads}, updated {updates}"));
        }

        return T($"Скачали вручную: {totalDownloads}\nОбновились автоматически: {totalUpdates}\n\n",
                 $"Downloaded manually: {totalDownloads}\nAuto-updated: {totalUpdates}\n\n") + lines;
    }
}
