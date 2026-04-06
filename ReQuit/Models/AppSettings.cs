using System.Text.Json.Serialization;

namespace ReQuit.Models;

public sealed class AppSettings
{
    [JsonPropertyName("ui")]
    public UiAppearanceSettings Ui { get; set; } = new();

    [JsonPropertyName("window")]
    public WindowSettings Window { get; set; } = new();

    [JsonPropertyName("timer")]
    public TimerSettings Timer { get; set; } = new();

    [JsonPropertyName("meta")]
    public SettingsMeta Meta { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Ui = new UiAppearanceSettings { Language = null },
        Window = new WindowSettings(),
        Timer = new TimerSettings { DelaySeconds = 60 },
        Meta = new SettingsMeta { SettingsFormatVersion = 1 }
    };
}

public sealed class TimerSettings
{
    public bool ActionIsSignal { get; set; }

    /// <summary>Отсрочка для режима выключения. В JSON сохраняется и при режиме «Сигнал», чтобы вернуть при переключении обратно. По умолчанию 1 минута.</summary>
    public int DelaySeconds { get; set; } = 60;

    /// <summary>Устар.: 0 — CPU, 1 — GPU, 2 — сеть. Используется только если <see cref="ResourceTriggerKey"/> пуст.</summary>
    public int ResourceConditionKind { get; set; }

    /// <summary>Ключ условия: <c>cpu</c>, <c>internet</c>, <c>gpu:N</c> (N — порядковый номер GPU, как в DXGI / счётчиках phys_N).</summary>
    public string? ResourceTriggerKey { get; set; }

    /// <summary>Порог в процентах (0–100): срабатывание при падении нагрузки выбранного ресурса строго ниже этого значения.</summary>
    public int ResourceThresholdPercent { get; set; } = 10;
}

public sealed class WindowSettings
{
    /// <summary>Крестик сворачивает в трей вместо выхода.</summary>
    public bool MinimizeToTrayOnCloseClick { get; set; }

    /// <summary>Старт с иконкой только в трее (окно скрыто).</summary>
    public bool MinimizeToTrayOnStartup { get; set; }

    public bool MinimizeToTrayAnimationEnabled { get; set; } = true;
}

public sealed class UiAppearanceSettings
{
    public string? Language { get; set; }
}

public sealed class SettingsMeta
{
    public int SettingsFormatVersion { get; set; } = 1;
}
