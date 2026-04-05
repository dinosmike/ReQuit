using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using ReQuit.Models;
using ReQuit.Services;
using WinFormsScreen = System.Windows.Forms.Screen;
using WpfButton = System.Windows.Controls.Button;

namespace ReQuit;

public partial class MainWindow : Window
{
    private enum TimerState { Idle, Running, DelayCountdown }
    private enum ActionKind { Shutdown, Signal }

    private TimerState _state = TimerState.Idle;
    private DispatcherTimer? _countdown;
    private int _remainingSeconds;

    private bool _actionIsSignal;
    private int _delaySeconds;

    private bool _trayHideAnimating;
    private bool _trayShowAnimating;

    private const int TrayFlyDurationMs = 400;
    private const double TrayFlyEndScale = 0.14;

    private const int PillPopupCloseDelayMs = 200;
    private DispatcherTimer? _closeActionPopupTimer;
    private DispatcherTimer? _closeDelayPopupTimer;

    private static readonly Thickness OptionCardBorderClosed = new(1, 1, 1, 1);
    private static readonly Thickness OptionCardBorderDropOpen = new(1, 1, 1, 0);

    private bool _actionMouseInCard;
    private bool _actionMouseInPill;
    private bool _delayMouseInCard;
    private bool _delayMouseInPill;

    public MainWindow()
    {
        InitializeComponent();

        HoursDigit.ValueChanged += OnAnyDigitChanged;
        MinutesDigit.ValueChanged += OnAnyDigitChanged;
        SecondsDigit.ValueChanged += OnAnyDigitChanged;

        var stored = SettingsStorage.Load().Timer;
        _actionIsSignal = stored.ActionIsSignal;
        _delaySeconds = stored.DelaySeconds;

        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
        Unloaded += MainWindow_OnUnloaded;

        RefreshLocalizedUi();
        UpdateButtonState();
        Loaded += (_, _) =>
        {
            ApplyActionOptionCardVisuals();
            ApplyDelayOptionCardVisuals();
        };
    }

    private void OnLocalizationLanguageChanged() => RefreshLocalizedUi();

    private void RefreshLocalizedUi()
    {
        HoursDigit.UnitText = LocalizationService.T("Timer.Hours");
        MinutesDigit.UnitText = LocalizationService.T("Timer.Minutes");
        SecondsDigit.UnitText = LocalizationService.T("Timer.Seconds");

        ActionPillText.Text = _actionIsSignal
            ? LocalizationService.T("MainWindow.ActionSignal")
            : LocalizationService.T("MainWindow.ActionShutdown");

        DelayPillText.Text = GetDelayLabelText(_delaySeconds);
        UpdateLangButtonLabel();
        ApplyCloseButtonAppearance();
        UpdateButtonState();
        UpdateStatusText();
        ApplyActionVisualTheme();
    }

    internal void ReloadWindowSettingsFromDisk() => ApplyCloseButtonAppearance();

    private void PersistTimerSettings()
    {
        var s = SettingsStorage.Load();
        s.Timer.ActionIsSignal = _actionIsSignal;
        s.Timer.DelaySeconds = _delaySeconds;
        SettingsStorage.Save(s);
    }

    private void ApplyActionVisualTheme()
    {
        bool shutdown = !_actionIsSignal;
        BtnStartStop.Style = shutdown
            ? (Style)FindResource("Rs.Button.Danger")
            : (Style)FindResource("Rs.Button.Accent");
        HoursDigit.ShutdownAction = shutdown;
        MinutesDigit.ShutdownAction = shutdown;
        SecondsDigit.ShutdownAction = shutdown;

        if (_state == TimerState.Running || _state == TimerState.DelayCountdown)
            StartColonBlink();
        else
            StopColonBlink();
    }

    private void ApplyCloseButtonAppearance()
    {
        var trayMode = SettingsStorage.Load().Window.MinimizeToTrayOnCloseClick;
        BtnClose.ToolTip = trayMode
            ? LocalizationService.T("MainWindow.TooltipCloseToTray")
            : LocalizationService.T("MainWindow.TooltipCloseExit");
        var key = trayMode ? "Rs.TitleBar.Button" : "Rs.TitleBar.Close";
        if (TryFindResource(key) is Style st)
            BtnClose.Style = st;
    }

    private static string GetDelayLabelText(int sec) => sec switch
    {
        0 => LocalizationService.T("MainWindow.DelayNone"),
        30 => LocalizationService.T("MainWindow.Delay30s"),
        60 => LocalizationService.T("MainWindow.Delay1m"),
        300 => LocalizationService.T("MainWindow.Delay5m"),
        _ => LocalizationService.T("MainWindow.DelaySecondsSuffix", sec)
    };

    private void UpdateLangButtonLabel()
    {
        var c = LocalizationService.CurrentLanguageCode;
        TitleBarLangButton.Content = string.Equals(c, "ru", StringComparison.OrdinalIgnoreCase) ? "Ru" : "En";
    }

    private ActionKind SelectedAction =>
        _actionIsSignal ? ActionKind.Signal : ActionKind.Shutdown;

    private int GetDelaySeconds() => _delaySeconds;

    private void ResetActionOptionCardChrome()
    {
        ActionOptionCard.CornerRadius = new CornerRadius(8);
        ActionOptionCard.BorderThickness = OptionCardBorderClosed;
    }

    private void OpenActionOptionCardDropChrome()
    {
        ActionOptionCard.CornerRadius = new CornerRadius(8, 8, 0, 0);
        ActionOptionCard.BorderThickness = OptionCardBorderDropOpen;
    }

    private void ResetDelayOptionCardChrome()
    {
        DelayOptionCard.CornerRadius = new CornerRadius(8);
        DelayOptionCard.BorderThickness = OptionCardBorderClosed;
    }

    private void OpenDelayOptionCardDropChrome()
    {
        DelayOptionCard.CornerRadius = new CornerRadius(8, 8, 0, 0);
        DelayOptionCard.BorderThickness = OptionCardBorderDropOpen;
    }

    private static void ClearActionOptionCardChrome(Border card)
    {
        card.Background = System.Windows.Media.Brushes.Transparent;
        card.BorderBrush = System.Windows.Media.Brushes.Transparent;
        card.BorderThickness = OptionCardBorderClosed;
        card.CornerRadius = new CornerRadius(8);
    }

    private static void ClearDelayOptionCardChrome(Border card)
    {
        card.Background = System.Windows.Media.Brushes.Transparent;
        card.BorderBrush = System.Windows.Media.Brushes.Transparent;
        card.BorderThickness = OptionCardBorderClosed;
        card.CornerRadius = new CornerRadius(8);
    }

    private void ApplyActionOptionCardVisuals()
    {
        bool cardChrome = ActionPopup.IsOpen || _actionMouseInCard;
        bool pillChrome = _actionMouseInPill;

        if (cardChrome)
        {
            ActionOptionCard.SetResourceReference(Border.BackgroundProperty, "Rs.OptionCard.Background");
            ActionOptionCard.SetResourceReference(Border.BorderBrushProperty, "Rs.OptionCard.Border");
            if (ActionPopup.IsOpen)
                OpenActionOptionCardDropChrome();
            else
                ResetActionOptionCardChrome();
        }
        else
            ClearActionOptionCardChrome(ActionOptionCard);

        var pillStyleKey = pillChrome
            ? "Rs.PillTrigger"
            : cardChrome
                ? "Rs.PillTriggerMuted"
                : "Rs.PillTriggerPlain";
        ActionPillTrigger.Style = (Style)FindResource(pillStyleKey);
    }

    private void ApplyDelayOptionCardVisuals()
    {
        bool cardChrome = DelayPopup.IsOpen || _delayMouseInCard;
        bool pillChrome = _delayMouseInPill;

        if (cardChrome)
        {
            DelayOptionCard.SetResourceReference(Border.BackgroundProperty, "Rs.OptionCard.Background");
            DelayOptionCard.SetResourceReference(Border.BorderBrushProperty, "Rs.OptionCard.Border");
            if (DelayPopup.IsOpen)
                OpenDelayOptionCardDropChrome();
            else
                ResetDelayOptionCardChrome();
        }
        else
            ClearDelayOptionCardChrome(DelayOptionCard);

        var delayPillStyleKey = pillChrome
            ? "Rs.PillTrigger"
            : cardChrome
                ? "Rs.PillTriggerMuted"
                : "Rs.PillTriggerPlain";
        DelayPillTrigger.Style = (Style)FindResource(delayPillStyleKey);
    }

    private void MainWindow_OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _closeActionPopupTimer?.Stop();
        _closeDelayPopupTimer?.Stop();
    }

    private void EnsureActionCloseTimer()
    {
        if (_closeActionPopupTimer != null)
            return;
        _closeActionPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PillPopupCloseDelayMs) };
        _closeActionPopupTimer.Tick += (_, _) =>
        {
            _closeActionPopupTimer!.Stop();
            ActionPopup.IsOpen = false;
            ApplyActionOptionCardVisuals();
        };
    }

    private void EnsureDelayCloseTimer()
    {
        if (_closeDelayPopupTimer != null)
            return;
        _closeDelayPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PillPopupCloseDelayMs) };
        _closeDelayPopupTimer.Tick += (_, _) =>
        {
            _closeDelayPopupTimer!.Stop();
            DelayPopup.IsOpen = false;
            ApplyDelayOptionCardVisuals();
        };
    }

    private void ScheduleCloseActionPopup()
    {
        EnsureActionCloseTimer();
        _closeActionPopupTimer!.Stop();
        _closeActionPopupTimer.Start();
    }

    private void ScheduleCloseDelayPopup()
    {
        EnsureDelayCloseTimer();
        _closeDelayPopupTimer!.Stop();
        _closeDelayPopupTimer.Start();
    }

    private void CancelCloseActionPopup() => _closeActionPopupTimer?.Stop();

    private void CancelCloseDelayPopup() => _closeDelayPopupTimer?.Stop();

    private void ActionOptionCard_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _actionMouseInCard = true;
        ApplyActionOptionCardVisuals();
    }

    private void ActionOptionCard_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _actionMouseInCard = false;
        ApplyActionOptionCardVisuals();
        ScheduleCloseActionPopup();
    }

    private void ActionPopupSurface_OnMouseEnter(object sender, MouseEventArgs e) => CancelCloseActionPopup();

    private void ActionPopupSurface_OnMouseLeave(object sender, MouseEventArgs e) => ScheduleCloseActionPopup();

    private void DelayOptionCard_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _delayMouseInCard = true;
        ApplyDelayOptionCardVisuals();
    }

    private void DelayOptionCard_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _delayMouseInCard = false;
        ApplyDelayOptionCardVisuals();
        ScheduleCloseDelayPopup();
    }

    private void ActionPill_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _actionMouseInPill = true;
        CancelCloseActionPopup();
        DelayPopup.IsOpen = false;
        CancelCloseDelayPopup();
        ApplyDelayOptionCardVisuals();
        ActionPopup.IsOpen = true;
        ApplyActionOptionCardVisuals();
    }

    private void ActionPill_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _actionMouseInPill = false;
        ApplyActionOptionCardVisuals();
    }

    private void DelayPill_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _delayMouseInPill = true;
        CancelCloseDelayPopup();
        ActionPopup.IsOpen = false;
        CancelCloseActionPopup();
        ApplyActionOptionCardVisuals();
        DelayPopup.IsOpen = true;
        ApplyDelayOptionCardVisuals();
    }

    private void DelayPill_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _delayMouseInPill = false;
        ApplyDelayOptionCardVisuals();
    }

    private void DelayPopupSurface_OnMouseEnter(object sender, MouseEventArgs e) => CancelCloseDelayPopup();

    private void DelayPopupSurface_OnMouseLeave(object sender, MouseEventArgs e) => ScheduleCloseDelayPopup();

    private void ActionPickShutdown_OnClick(object sender, RoutedEventArgs e)
    {
        bool wasSignal = _actionIsSignal;
        _actionIsSignal = false;
        ActionPillText.Text = LocalizationService.T("MainWindow.ActionShutdown");
        if (_state == TimerState.DelayCountdown && wasSignal)
            ScheduleSystemShutdown(_remainingSeconds);
        CancelCloseActionPopup();
        ActionPopup.IsOpen = false;
        ApplyActionOptionCardVisuals();
        ApplyActionVisualTheme();
        PersistTimerSettings();
    }

    private void ActionPickSignal_OnClick(object sender, RoutedEventArgs e)
    {
        bool wasShutdown = !_actionIsSignal;
        _actionIsSignal = true;
        ActionPillText.Text = LocalizationService.T("MainWindow.ActionSignal");
        if (_state == TimerState.DelayCountdown && wasShutdown)
            AbortSystemShutdown();
        CancelCloseActionPopup();
        ActionPopup.IsOpen = false;
        ApplyActionOptionCardVisuals();
        ApplyActionVisualTheme();
        PersistTimerSettings();
    }

    private void DelayPick_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton b || b.Tag is not string tag || !int.TryParse(tag, out int sec))
            return;

        _delaySeconds = sec;
        DelayPillText.Text = GetDelayLabelText(sec);
        CancelCloseDelayPopup();
        DelayPopup.IsOpen = false;
        ApplyDelayOptionCardVisuals();
        PersistTimerSettings();

        if (_state != TimerState.DelayCountdown)
            return;

        if (sec == 0)
        {
            AbortSystemShutdown();
            _countdown?.Stop();
            _countdown = null;
            ExecuteAction();
            return;
        }

        _remainingSeconds = sec;
        if (SelectedAction == ActionKind.Shutdown)
        {
            AbortSystemShutdown();
            ScheduleSystemShutdown(sec);
        }
        UpdateTimerDisplay();
        UpdateStatusText();
    }

    private void OnAnyDigitChanged()
    {
        if (_state != TimerState.Idle) return;
        RecalcRemaining();
        UpdateStartEnabled();
    }

    private void RecalcRemaining()
    {
        _remainingSeconds = HoursDigit.Value * 3600
                          + MinutesDigit.Value * 60
                          + SecondsDigit.Value;
    }

    private int GetTotalFromDigits() =>
        HoursDigit.Value * 3600 + MinutesDigit.Value * 60 + SecondsDigit.Value;

    private void UpdateStartEnabled()
    {
        if (_state != TimerState.Idle) return;
        BtnStartStop.IsEnabled = GetTotalFromDigits() > 0;
    }

    private void StartTimer()
    {
        RecalcRemaining();
        if (_remainingSeconds <= 0) return;

        _state = TimerState.Running;
        SetDigitsReadOnly(true);
        SetOptionsEnabled(false);

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += OnCountdownTick;
        _countdown.Start();

        UpdateButtonState();
        UpdateStatusText();
        StartColonBlink();
    }

    private void StopTimer()
    {
        bool wasDelay = _state == TimerState.DelayCountdown;
        _state = TimerState.Idle;
        _countdown?.Stop();
        _countdown = null;
        SetDigitsReadOnly(false);
        SetOptionsEnabled(true);
        UpdateButtonState();
        UpdateStatusText();
        StopColonBlink();
        UpdateStartEnabled();

        if (wasDelay && SelectedAction == ActionKind.Shutdown)
            AbortSystemShutdown();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            _remainingSeconds = 0;
            UpdateTimerDisplay();
            _countdown?.Stop();
            _countdown = null;

            int delaySec = GetDelaySeconds();
            if (delaySec > 0)
            {
                StartDelayCountdown(delaySec);
            }
            else
            {
                ExecuteAction();
            }
            return;
        }

        UpdateTimerDisplay();
    }

    private void StartDelayCountdown(int delaySec)
    {
        if (SelectedAction == ActionKind.Shutdown)
            ScheduleSystemShutdown(delaySec);

        _remainingSeconds = delaySec;
        _state = TimerState.DelayCountdown;

        UpdateTimerDisplay();
        UpdateButtonState();
        UpdateStatusText();

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += OnDelayTick;
        _countdown.Start();
    }

    private void OnDelayTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            _remainingSeconds = 0;
            UpdateTimerDisplay();
            _countdown?.Stop();
            _countdown = null;

            if (SelectedAction == ActionKind.Signal)
                ExecuteAction();

            _state = TimerState.Idle;
            SetDigitsReadOnly(false);
            SetOptionsEnabled(true);
            UpdateButtonState();
            StopColonBlink();
            UpdateStartEnabled();
            StatusText.Text = "";
            return;
        }

        UpdateTimerDisplay();
        UpdateStatusText();
    }

    private void ExecuteAction()
    {
        switch (SelectedAction)
        {
            case ActionKind.Shutdown:
                ScheduleSystemShutdown(0);
                _state = TimerState.Idle;
                SetDigitsReadOnly(false);
                SetOptionsEnabled(true);
                UpdateButtonState();
                StopColonBlink();
                UpdateStartEnabled();
                StatusText.Text = "";
                break;

            case ActionKind.Signal:
                PlayAlarmSignal();
                _state = TimerState.Idle;
                SetDigitsReadOnly(false);
                SetOptionsEnabled(true);
                UpdateButtonState();
                StopColonBlink();
                UpdateStartEnabled();
                StatusText.Text = "";
                break;
        }
    }

    private void UpdateTimerDisplay()
    {
        int h = _remainingSeconds / 3600;
        int m = (_remainingSeconds % 3600) / 60;
        int s = _remainingSeconds % 60;

        if (h != HoursDigit.Value) HoursDigit.AnimateToValue(h, slideDown: true);
        if (m != MinutesDigit.Value) MinutesDigit.AnimateToValue(m, slideDown: true);
        if (s != SecondsDigit.Value) SecondsDigit.AnimateToValue(s, slideDown: true);
    }

    private void SetDigitsReadOnly(bool ro)
    {
        HoursDigit.IsReadOnly = ro;
        MinutesDigit.IsReadOnly = ro;
        SecondsDigit.IsReadOnly = ro;
    }

    private void SetOptionsEnabled(bool enabled)
    {
        if (!enabled)
        {
            CancelCloseActionPopup();
            CancelCloseDelayPopup();
            ActionPopup.IsOpen = false;
            DelayPopup.IsOpen = false;
        }
        ApplyActionOptionCardVisuals();
        ApplyDelayOptionCardVisuals();
    }

    // ── System commands ─────────────────────────────────────

    private static void ScheduleSystemShutdown(int delaySec)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", $"/s /t {delaySec}")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    private static void AbortSystemShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/a")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    private static void PlayAlarmSignal()
    {
        try
        {
            SystemSounds.Exclamation.Play();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            int count = 0;
            timer.Tick += (_, _) =>
            {
                count++;
                if (count >= 4)
                {
                    timer.Stop();
                    return;
                }
                SystemSounds.Exclamation.Play();
            };
            timer.Start();
        }
        catch { }
    }

    // ── UI state ────────────────────────────────────────────

    private void UpdateButtonState()
    {
        switch (_state)
        {
            case TimerState.Idle:
                BtnStartStop.Content = LocalizationService.T("MainWindow.BtnStart");
                break;
            case TimerState.Running:
            case TimerState.DelayCountdown:
                BtnStartStop.Content = LocalizationService.T("MainWindow.BtnStop");
                BtnStartStop.IsEnabled = true;
                break;
        }

        if (_state == TimerState.Idle)
            UpdateStartEnabled();
    }

    private void UpdateStatusText()
    {
        if (_state == TimerState.DelayCountdown)
        {
            var actionWord = SelectedAction == ActionKind.Shutdown
                ? LocalizationService.T("MainWindow.StatusDelayShutdown")
                : LocalizationService.T("MainWindow.StatusDelaySignal");
            StatusText.Text = LocalizationService.T("MainWindow.StatusDelayFormat", actionWord, _remainingSeconds);
            return;
        }

        StatusText.Text = "";
    }

    // ── Colon blink animation ───────────────────────────────

    private void StartColonBlink()
    {
        StopColonBlink();

        var colonBrush = !_actionIsSignal
            ? (Brush)FindResource("Rs.Danger")
            : (Brush)FindResource("Rs.Text.Primary");
        Colon1.Foreground = colonBrush;
        Colon2.Foreground = colonBrush;

        var anim = new DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Colon1.BeginAnimation(OpacityProperty, anim);
        Colon2.BeginAnimation(OpacityProperty, anim);
    }

    private void StopColonBlink()
    {
        Colon1.BeginAnimation(OpacityProperty, null);
        Colon2.BeginAnimation(OpacityProperty, null);
        Colon1.Opacity = 0.5;
        Colon2.Opacity = 0.5;

        var colonBrush = !_actionIsSignal
            ? (Brush)FindResource("Rs.Danger")
            : (Brush)FindResource("Rs.Text.Primary");
        Colon1.Foreground = colonBrush;
        Colon2.Foreground = colonBrush;
    }

    // ── Button handlers ─────────────────────────────────────

    private void BtnStartStop_OnClick(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case TimerState.Idle:
                StartTimer();
                break;
            case TimerState.Running:
            case TimerState.DelayCountdown:
                StopTimer();
                break;
        }
    }

    private void TitleBar_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void TitleBarAbout_OnClick(object sender, RoutedEventArgs e) => About.Show(this);

    private void TitleBarAbout_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        About.Show(this);
    }

    private void BtnMinimize_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();

    private void TitleBarMinimizeToTray_OnClick(object sender, RoutedEventArgs e) =>
        HideToTrayCore(SettingsStorage.Load());

    private void TitleBarSettings_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowSingletonOrActivate(dlg =>
        {
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        });
    }

    internal void HideToTrayOnStartup() => Hide();

    /// <summary>Скрывает окно в трей с опциональной анимацией.</summary>
    private void HideToTrayCore(AppSettings s)
    {
        if (!s.Window.MinimizeToTrayAnimationEnabled)
        {
            Hide();
            return;
        }

        if (_trayHideAnimating || _trayShowAnimating)
            return;

        _trayHideAnimating = true;
        var origLeft = Left;
        var origTop = Top;
        GetFlyToTrayTarget(origLeft, origTop, out var endLeft, out var endTop);

        var duration = TimeSpan.FromMilliseconds(TrayFlyDurationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        var sb = new Storyboard();

        var animLeft = new DoubleAnimation(origLeft, endLeft, duration) { EasingFunction = easing };
        Storyboard.SetTarget(animLeft, this);
        Storyboard.SetTargetProperty(animLeft, new PropertyPath(LeftProperty));
        sb.Children.Add(animLeft);

        var animTop = new DoubleAnimation(origTop, endTop, duration) { EasingFunction = easing };
        Storyboard.SetTarget(animTop, this);
        Storyboard.SetTargetProperty(animTop, new PropertyPath(TopProperty));
        sb.Children.Add(animTop);

        var fade = new DoubleAnimation(Opacity, 0, duration) { EasingFunction = easing };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        sb.Children.Add(fade);

        var sx = new DoubleAnimation(TrayHideScale.ScaleX, TrayFlyEndScale, duration) { EasingFunction = easing };
        Storyboard.SetTarget(sx, TrayHideScale);
        Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
        sb.Children.Add(sx);

        var sy = new DoubleAnimation(TrayHideScale.ScaleY, TrayFlyEndScale, duration) { EasingFunction = easing };
        Storyboard.SetTarget(sy, TrayHideScale);
        Storyboard.SetTargetProperty(sy, new PropertyPath(ScaleTransform.ScaleYProperty));
        sb.Children.Add(sy);

        sb.Completed += (_, _) =>
        {
            _trayHideAnimating = false;
            sb.Remove(this);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = origLeft;
            Top = origTop;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            TrayHideScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            TrayHideScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            TrayHideScale.ScaleX = 1;
            TrayHideScale.ScaleY = 1;
            Hide();
        };

        sb.Begin(this, true);
    }

    internal void ShowFromTray()
    {
        ApplyCloseButtonAppearance();

        if (WindowState == WindowState.Minimized)
        {
            ShowCore();
            return;
        }

        if (IsVisible)
        {
            WindowState = WindowState.Normal;
            Topmost = true;
            Topmost = false;
            Activate();
            return;
        }

        var s = SettingsStorage.Load();
        if (!s.Window.MinimizeToTrayAnimationEnabled)
        {
            ShowCore();
            return;
        }

        if (_trayShowAnimating || _trayHideAnimating)
            return;

        var endLeft = Left;
        var endTop = Top;
        GetFlyToTrayTarget(endLeft, endTop, out var startLeft, out var startTop);

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        TrayHideScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        TrayHideScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        Left = startLeft;
        Top = startTop;
        Opacity = 0;
        TrayHideScale.ScaleX = 1;
        TrayHideScale.ScaleY = 1;

        Show();
        WindowState = WindowState.Normal;

        _trayShowAnimating = true;
        var duration = TimeSpan.FromMilliseconds(TrayFlyDurationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sb = new Storyboard();

        var animLeft = new DoubleAnimation(startLeft, endLeft, duration) { EasingFunction = easing };
        Storyboard.SetTarget(animLeft, this);
        Storyboard.SetTargetProperty(animLeft, new PropertyPath(LeftProperty));
        sb.Children.Add(animLeft);

        var animTop = new DoubleAnimation(startTop, endTop, duration) { EasingFunction = easing };
        Storyboard.SetTarget(animTop, this);
        Storyboard.SetTargetProperty(animTop, new PropertyPath(TopProperty));
        sb.Children.Add(animTop);

        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        sb.Children.Add(fade);

        sb.Completed += (_, _) =>
        {
            _trayShowAnimating = false;
            sb.Remove(this);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = endLeft;
            Top = endTop;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            TrayHideScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            TrayHideScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            TrayHideScale.ScaleX = 1;
            TrayHideScale.ScaleY = 1;
            Topmost = true;
            Topmost = false;
            Activate();
        };

        sb.Begin(this, true);
    }

    private void ShowCore()
    {
        Show();
        WindowState = WindowState.Normal;
        Topmost = true;
        Topmost = false;
        Activate();
    }

    private void GetFlyToTrayTarget(double origLeft, double origTop, out double endLeft, out double endTop)
    {
        const double horizontalFactor = 0.38;
        const double verticalFactor = 1.0;

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;

        double cornerLeft;
        double cornerTop;

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            try
            {
                var handle = new WindowInteropHelper(this).EnsureHandle();
                var wa = WinFormsScreen.FromHandle(handle).WorkingArea;
                var tf = src.CompositionTarget.TransformFromDevice;
                var br = tf.Transform(new System.Windows.Point(wa.Right, wa.Bottom));
                cornerLeft = br.X - w;
                cornerTop = br.Y - h;
                endLeft = origLeft + (cornerLeft - origLeft) * horizontalFactor;
                endTop = origTop + (cornerTop - origTop) * verticalFactor;
                return;
            }
            catch
            {
                // fallback below
            }
        }

        var waDip = SystemParameters.WorkArea;
        cornerLeft = waDip.Right - w;
        cornerTop = waDip.Bottom - h;
        endLeft = origLeft + (cornerLeft - origLeft) * horizontalFactor;
        endTop = origTop + (cornerTop - origTop) * verticalFactor;
    }

    private void TitleBarLanguage_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton b && b.ContextMenu != null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void LanguageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag)
            return;
        var code = AppLanguageCatalog.Normalize(tag);
        LocalizationService.Apply(code);
        var settings = SettingsStorage.Load();
        settings.Ui.Language = code;
        SettingsStorage.Save(settings);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.ShutdownRequested)
        {
            if (_state == TimerState.DelayCountdown && SelectedAction == ActionKind.Shutdown)
                AbortSystemShutdown();
            _countdown?.Stop();
            _countdown = null;
            base.OnClosing(e);
            return;
        }

        if (_state == TimerState.Running || _state == TimerState.DelayCountdown)
        {
            var result = MessageBox.Show(
                LocalizationService.T("MainWindow.ConfirmCloseActive"),
                LocalizationService.T("Common.AppTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            StopTimer();
        }

        var s = SettingsStorage.Load();
        if (s.Window.MinimizeToTrayOnCloseClick)
        {
            e.Cancel = true;
            HideToTrayCore(s);
            return;
        }

        if (_state == TimerState.DelayCountdown && SelectedAction == ActionKind.Shutdown)
            AbortSystemShutdown();
        _countdown?.Stop();
        _countdown = null;
        base.OnClosing(e);
    }
}
