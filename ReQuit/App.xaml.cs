using System.Windows;
using ReQuit.Models;
using ReQuit.Services;

namespace ReQuit;

public partial class App : Application
{
    /// <summary>Истинно при «Выход» из трея — чтобы не отменять закрытие в <see cref="MainWindow.OnClosing"/>.</summary>
    internal static bool ShutdownRequested { get; set; }

    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsStorage.Load();
        var hadStoredLanguage = !string.IsNullOrWhiteSpace(settings.Ui.Language);
        var lang = hadStoredLanguage
            ? AppLanguageCatalog.Normalize(settings.Ui.Language)
            : AppLanguageCatalog.ResolveDefaultLanguage();
        LocalizationService.Apply(lang);

        if (!hadStoredLanguage)
        {
            settings.Ui.Language = lang;
            SettingsStorage.Save(settings);
        }

        MainWindow = new MainWindow();
        MainWindow.Show();

        _tray = new TrayService();
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;

        if (SettingsStorage.Load().Window.MinimizeToTrayOnStartup && MainWindow is MainWindow mw)
            mw.HideToTrayOnStartup();
    }

    private void OnLocalizationLanguageChanged() => _tray?.RefreshTexts();

    internal void RefreshTrayMenu() => _tray?.RefreshTexts();

    internal void SyncTrayCountdownIcon(bool timerActive, int remainingSeconds, bool actionIsSignal, bool resourceConditionWait = false) =>
        _tray?.SyncCountdownIcon(timerActive, remainingSeconds, actionIsSignal, resourceConditionWait);

    protected override void OnExit(ExitEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
