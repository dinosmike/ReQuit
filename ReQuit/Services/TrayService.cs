using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using ReQuit;

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

    /// <summary>Выход из трея: только с потока WPF, через <see cref="MainWindow.Close"/>, иначе контекстное меню WinForms
    /// вызывает закрытие с чужого потока — не показывается <see cref="ConfirmCloseWindow"/> при работающем таймере.</summary>
    private static void ExitApp()
    {
        var app = Application.Current;
        if (app?.Dispatcher == null)
            return;

        if (app.Dispatcher.CheckAccess())
            ExitAppOnUiThread();
        else
            app.Dispatcher.Invoke(ExitAppOnUiThread, DispatcherPriority.Send);
    }

    private static void ExitAppOnUiThread()
    {
        App.ShutdownRequested = true;
        if (Application.Current?.MainWindow is MainWindow mw)
            mw.Close();
        else
            Application.Current?.Shutdown();
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
