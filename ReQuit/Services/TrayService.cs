using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace ReQuit.Services;

public sealed class TrayService : IDisposable
{
    private static int SingleClickDelayMs =>
        Math.Clamp(SystemInformation.DoubleClickTime + 40, 200, 800);

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _singleClickTimer;

    public TrayService()
    {
        _singleClickTimer = new System.Windows.Forms.Timer { Interval = SingleClickDelayMs };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            InvokeOnUi(() => ShowMain());
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIconFactory.CreateTrayIcon(),
            Text = LocalizationService.T("Tray.BalloonTip"),
            Visible = true
        };
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;

        _notifyIcon.ContextMenuStrip = BuildContextMenu();
    }

    public void RefreshTexts()
    {
        _notifyIcon.Text = LocalizationService.T("Tray.BalloonTip");
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        oldMenu?.Dispose();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.T("Tray.MenuOpen"), null, (_, _) => ShowMain());
        menu.Items.Add(LocalizationService.T("Tray.MenuSettings"), null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(LocalizationService.T("Tray.MenuExit"), null, (_, _) => ExitApp());
        return menu;
    }

    private void OnNotifyIconMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
            return;
        _singleClickTimer.Interval = SingleClickDelayMs;
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
    }

    private void OnMouseDoubleClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
            return;
        _singleClickTimer.Stop();
        InvokeOnUi(ShowMain);
    }

    private static void InvokeOnUi(Action action)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted)
            return;
        if (disp.CheckAccess())
            action();
        else
            disp.BeginInvoke(DispatcherPriority.Input, action);
    }

    private static void ShowMain()
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.ShowFromTray();
    }

    private static void ShowSettings()
    {
        SettingsWindow.ShowSingletonOrActivate(dlg =>
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                dlg.Owner = mw;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dlg.Owner = null;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        });
    }

    private static void ExitApp()
    {
        App.ShutdownRequested = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _singleClickTimer.Stop();
        _singleClickTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
