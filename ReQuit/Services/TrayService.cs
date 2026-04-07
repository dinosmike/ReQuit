using System.Drawing;
using System.Globalization;
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

    /// <summary>Кэш последнего вида иконки, чтобы не пересоздавать при каждом тике.</summary>
    private string _trayIconKey = "default";

    // Remember last timer state so language switch keeps correct tooltip.
    private bool _lastTimerActive;
    private int _lastRemainingSeconds;
    private bool _lastActionIsSignal;
    private bool _lastResourceConditionWait;

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
        ApplyTooltip(_lastTimerActive, _lastRemainingSeconds, _lastResourceConditionWait);
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        oldMenu?.Dispose();
    }

    /// <summary>
    /// Таймер активен: &gt; 60 мин — «Nч»/«Nh»; иначе минуты 1…60 или секунды 00…59.
    /// Выключение — красный; сигнал — #01a16a. Иначе — обычная иконка приложения.
    /// </summary>
    public void SyncCountdownIcon(bool timerActive, int remainingSeconds, bool actionIsSignal, bool resourceConditionWait = false)
    {
        _lastTimerActive = timerActive;
        _lastRemainingSeconds = remainingSeconds;
        _lastActionIsSignal = actionIsSignal;
        _lastResourceConditionWait = resourceConditionWait;

        ApplyTooltip(timerActive, remainingSeconds, resourceConditionWait);

        if (!timerActive)
        {
            if (_trayIconKey == "default")
                return;
            ApplyTrayIcon("default", AppIconFactory.CreateTrayIcon());
            return;
        }

        var mode = actionIsSignal ? "sig" : "sd";
        string key;
        string glyph;

        if (resourceConditionWait)
        {
            key = $"{mode}:reswait";
            glyph = "..";
            if (key == _trayIconKey)
                return;
            ApplyTrayIcon(key, AppIconFactory.CreateTrayCountdownGlyph(glyph, actionIsSignal));
            return;
        }

        if (remainingSeconds > 3600)
        {
            int h = remainingSeconds / 3600;
            var suffix = LocalizationService.T("Tray.HourSuffix");
            glyph = h.ToString(CultureInfo.InvariantCulture) + suffix;
            key = $"{mode}:h:{h}:{LocalizationService.CurrentLanguageCode}";
        }
        else if (remainingSeconds >= 60)
        {
            int m = remainingSeconds / 60;
            glyph = m.ToString(CultureInfo.InvariantCulture);
            key = $"{mode}:m:{m}";
        }
        else
        {
            glyph = remainingSeconds.ToString("D2", CultureInfo.InvariantCulture);
            key = $"{mode}:s:{remainingSeconds}";
        }

        if (key == _trayIconKey)
            return;

        ApplyTrayIcon(key, AppIconFactory.CreateTrayCountdownGlyph(glyph, actionIsSignal));
    }

    private void ApplyTooltip(bool timerActive, int remainingSeconds, bool resourceConditionWait)
    {
        string text;
        if (!timerActive)
            text = LocalizationService.T("Tray.BalloonTip");
        else if (resourceConditionWait)
            text = LocalizationService.T("Tray.TooltipResourceWait");
        else
            text = LocalizationService.T("Tray.TooltipRemaining", FormatExactRemaining(remainingSeconds));

        // NotifyIcon.Text is limited (typically 63 chars). Keep it safe.
        if (text.Length > 63)
            text = text.Substring(0, 63);

        if (!string.Equals(_notifyIcon.Text, text, StringComparison.Ordinal))
            _notifyIcon.Text = text;
    }

    private static string FormatExactRemaining(int totalSeconds)
    {
        if (totalSeconds < 0)
            totalSeconds = 0;
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        return $"{h:D2}:{m:D2}:{s:D2}";
    }

    private void ApplyTrayIcon(string key, Icon newIcon)
    {
        _trayIconKey = key;
        var old = _notifyIcon.Icon;
        _notifyIcon.Icon = newIcon;
        old?.Dispose();
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
