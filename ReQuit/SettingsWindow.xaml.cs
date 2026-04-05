using System.Windows;
using System.Windows.Input;
using ReQuit.Services;

namespace ReQuit;

public partial class SettingsWindow
{
    private static SettingsWindow? _instance;
    private bool _trayOptionsLoading;

    internal static void ShowSingletonOrActivate(Action<SettingsWindow> configure)
    {
        var app = Application.Current;
        if (app?.Dispatcher == null)
            return;

        void Run()
        {
            if (_instance != null)
            {
                _instance.Activate();
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Focus();
                return;
            }

            var dlg = new SettingsWindow();
            configure(dlg);
            _instance = dlg;
            dlg.Closed += (_, _) =>
            {
                if (_instance == dlg)
                    _instance = null;
            };
            dlg.Show();
        }

        if (app.Dispatcher.CheckAccess())
            Run();
        else
            app.Dispatcher.Invoke(Run);
    }

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadTrayOptions();
    }

    private void LoadTrayOptions()
    {
        _trayOptionsLoading = true;
        try
        {
            var s = SettingsStorage.Load();
            TrayCloseCheck.IsChecked = s.Window.MinimizeToTrayOnCloseClick;
            TrayStartupCheck.IsChecked = s.Window.MinimizeToTrayOnStartup;
            TrayAnimCheck.IsChecked = s.Window.MinimizeToTrayAnimationEnabled;
        }
        finally
        {
            _trayOptionsLoading = false;
        }
    }

    private void TrayOption_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_trayOptionsLoading)
            return;

        var s = SettingsStorage.Load();
        s.Window.MinimizeToTrayOnCloseClick = TrayCloseCheck.IsChecked == true;
        s.Window.MinimizeToTrayOnStartup = TrayStartupCheck.IsChecked == true;
        s.Window.MinimizeToTrayAnimationEnabled = TrayAnimCheck.IsChecked == true;
        SettingsStorage.Save(s);

        if (Application.Current.MainWindow is MainWindow mw)
            mw.ReloadWindowSettingsFromDisk();
    }

    private void TitleBar_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void TitleBarClose_OnClick(object sender, RoutedEventArgs e) => Close();
}
