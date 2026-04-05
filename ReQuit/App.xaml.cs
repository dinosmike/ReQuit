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
        var lang = string.IsNullOrWhiteSpace(settings.Ui.Language)
            ? AppLanguageCatalog.ResolveDefaultLanguage()
            : AppLanguageCatalog.Normalize(settings.Ui.Language);
        LocalizationService.Apply(lang);

        MainWindow = new MainWindow();
        MainWindow.Show();

        _tray = new TrayService();
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;

        if (SettingsStorage.Load().Window.MinimizeToTrayOnStartup && MainWindow is MainWindow mw)
            mw.HideToTrayOnStartup();
    }

    private void OnLocalizationLanguageChanged() => _tray?.RefreshTexts();

    internal void RefreshTrayMenu() => _tray?.RefreshTexts();

    protected override void OnExit(ExitEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
