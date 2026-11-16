using System.IO;
using System.Reflection;
using System.Windows.Media;

namespace ReUpdate;

/// <summary>
/// Всё, что модуль ReUpdate знает о конкретной программе. Заполняется один раз при старте
/// (см. «КАК ПОДКЛЮЧИТЬ.md»); остальные файлы папки от программы не зависят.
/// </summary>
public static class ReUpdateConfig
{
    /// <summary>Владелец и репозиторий на GitHub, например «dinosmike» и «ReFlow».</summary>
    public static string Owner { get; set; } = "";
    public static string Repo { get; set; } = "";

    /// <summary>Имя exe в релизе (он же имя запущенного файла).</summary>
    public static string AssetName { get; set; } = "";

    /// <summary>
    /// Копия exe в релизе только для автообновления, например «ReFlow-update.exe». Её счётчик скачиваний на GitHub
    /// показывает, сколько программ обновилось. Если такого файла в релизе нет — качается <see cref="AssetName"/>.
    /// </summary>
    public static string UpdateAssetName => Path.GetFileNameWithoutExtension(AssetName) + "-update" + Path.GetExtension(AssetName);

    /// <summary>Папка настроек в %LocalAppData% — там модуль хранит «что уже видели».</summary>
    public static string AppDataFolderName { get; set; } = "";

    /// <summary>Язык окна «Что нового»: «ru» или «en».</summary>
    public static Func<string> Language { get; set; } = () => "ru";

    /// <summary>Галочка «Проверять обновления».</summary>
    public static Func<bool> AutoUpdateEnabled { get; set; } = () => true;

    /// <summary>Цвета окна «Что нового» — из темы программы.</summary>
    public static ReUpdateColors Colors { get; set; } = new();

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    internal static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    internal static bool IsRussian => string.Equals(Language(), "ru", StringComparison.OrdinalIgnoreCase);
}

public sealed class ReUpdateColors
{
    public Color Background { get; set; } = Color.FromRgb(0x0D, 0x11, 0x17);
    public Color Surface { get; set; } = Color.FromRgb(0x16, 0x1B, 0x22);
    public Color Border { get; set; } = Color.FromRgb(0x30, 0x36, 0x3D);
    public Color Text { get; set; } = Color.FromRgb(0xE6, 0xED, 0xF3);
    public Color TextMuted { get; set; } = Color.FromRgb(0x8B, 0x94, 0x9E);
    public Color Accent { get; set; } = Color.FromRgb(0x3D, 0xD4, 0xC3);
    public Color BadgeBackground { get; set; } = Color.FromRgb(0x12, 0x2A, 0x28);
}
