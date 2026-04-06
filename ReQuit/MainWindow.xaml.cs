using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using NAudio.Wave;
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
    private enum TimerState { Idle, Running, DelayCountdown, ResourceWaiting, SignalFinished }
    private enum ActionKind { Shutdown, Signal }

    private TimerState _state = TimerState.Idle;
    private DispatcherTimer? _countdown;
    private int _remainingSeconds;

    private bool _actionIsSignal;
    private int _delaySeconds;

    private bool _trayHideAnimating;
    private bool _trayShowAnimating;

    /// <summary>Пока true — не дублировать оверлей отсрочки из <see cref="ShowFromTray"/> (его сразу создаёт <see cref="ShowDelayOverlay"/>).</summary>
    private bool _suppressEnsureDelayOverlayAfterShowFromTray;

    private const int TrayFlyDurationMs = 400;
    private const double TrayFlyEndScale = 0.14;

    private const int PillPopupCloseDelayMs = 200;
    /// <summary>Половина цикла мигания «:» (вторая половина — AutoReverse); в сумме ровно 1 с на один полный цикл.</summary>
    private const int ColonPulseHalfDurationMs = 500;
    private DispatcherTimer? _closeActionPopupTimer;
    private DispatcherTimer? _closeDelayPopupTimer;

    private DelayOverlayWindow? _delayOverlay;

    private const uint WmNcLButtonDown = 0xA1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Воспроизведение встроенного MP3 из памяти (NAudio), без файлов на диске.</summary>
    private static readonly object SignalPlaybackLock = new();

    private static IWavePlayer? _signalWaveOut;
    private static Mp3FileReader? _signalMp3Reader;
    private static MemoryStream? _signalMp3Memory;

    private static readonly Thickness OptionCardBorderClosed = new(1, 1, 1, 1);
    private static readonly Thickness OptionCardBorderDropOpen = new(1, 1, 1, 0);

    private bool _actionMouseInCard;
    private bool _actionMouseInPill;
    private bool _delayMouseInCard;
    private bool _delayMouseInPill;

    /// <summary>Для анимации смены «Старт» ↔ «Стоп»: null до первого UpdateButtonState.</summary>
    private bool? _wasShowingStopButton;

    private Storyboard? _signalEqualizerStoryboard;

    private ResourceMonitorSampler? _resourceSampler;
    private DispatcherTimer? _resourcePollTimer;
    private bool _suppressResourceComboEvents;
    private bool _suppressResourceThresholdEvents;
    private string _resourceWatchKey = ResourceTriggerKeys.Cpu;
    private int _resourceThresholdPercent = 10;
    private int _resourceWatchH;
    private int _resourceWatchM;
    private int _resourceWatchS;
    private float _prevResourceValueForEdge;
    private bool _resourceEdgePrimed;

    private const int ResourceSparkMaxPoints = 56;
    private const double ResourceSparkCanvasW = 276;
    private const double ResourceSparkCanvasH = 80;
    private readonly List<float> _resourceSparkSamples = new();
    private Storyboard? _resourceSparkScanStoryboard;
    private Storyboard? _resourceSparkPulseStoryboard;

    /// <summary>Опрос ресурсов не должен выполняться в UI-потоке — иначе подвисания каждые 500 мс.</summary>
    private bool _resourcePollBusy;

    public MainWindow()
    {
        InitializeComponent();

        ResourceMonitorOverlay.IsVisibleChanged += ResourceMonitorOverlay_OnIsVisibleChanged;

        HoursDigit.ValueChanged += OnAnyDigitChanged;
        MinutesDigit.ValueChanged += OnAnyDigitChanged;
        SecondsDigit.ValueChanged += OnAnyDigitChanged;

        var stored = SettingsStorage.Load().Timer;
        _actionIsSignal = stored.ActionIsSignal;
        _delaySeconds = stored.DelaySeconds;
        if (_actionIsSignal)
            _delaySeconds = 0;

        _resourceThresholdPercent = Math.Clamp(stored.ResourceThresholdPercent, 0, 100);
        _resourceWatchKey = MigrateResourceTriggerKey(stored);

        RecalcRemaining();

        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
        Unloaded += MainWindow_OnUnloaded;

        RefreshLocalizedUi();
        UpdateButtonState();
        Loaded += (_, _) =>
        {
            ApplyActionOptionCardVisuals();
            UpdateDelayOptionCardVisibility();
            ApplyDelayOptionCardVisuals();
            InitResourceMonitorCombos();
        };

        PreviewMouseRightButtonDown += MainWindow_PreviewMouseRightButtonDown;

        // TEMP: превью окна отсрочки по клавише Q — удалить вместе с регионом TEMP ниже
        PreviewKeyDown += MainWindow_PreviewKeyDown_TempDelayOverlayTest;
    }

    private void OnLocalizationLanguageChanged()
    {
        RefreshLocalizedUi();
        SyncTrayCountdownIcon();
    }

    private void RefreshLocalizedUi()
    {
        HoursDigit.UnitText = LocalizationService.T("Timer.Hours");
        MinutesDigit.UnitText = LocalizationService.T("Timer.Minutes");
        SecondsDigit.UnitText = LocalizationService.T("Timer.Seconds");
        ExactTimeHours.UnitText = LocalizationService.T("Timer.Hours");
        ExactTimeMinutes.UnitText = LocalizationService.T("Timer.Minutes");

        ActionPillText.Text = _actionIsSignal
            ? LocalizationService.T("MainWindow.ActionSignal")
            : LocalizationService.T("MainWindow.ActionShutdown");

        DelayPillText.Text = GetDelayLabelText(_delaySeconds);
        UpdateLangButtonLabel();
        ApplyCloseButtonAppearance();
        UpdateButtonState();
        UpdateStatusText();
        ApplyActionVisualTheme();
        ApplyExactTimeDigitTheme();
        UpdateDelayOptionCardVisibility();
        InitResourceMonitorCombos();
    }

    private void UpdateDelayOptionCardVisibility()
    {
        if (_actionIsSignal)
        {
            CancelCloseDelayPopup();
            DelayPopup.IsOpen = false;
            DelayOptionCard.Visibility = Visibility.Hidden;
            DelayOptionCard.IsHitTestVisible = false;
        }
        else
        {
            DelayOptionCard.Visibility = Visibility.Visible;
            DelayOptionCard.IsHitTestVisible = true;
        }
    }

    private void PersistTimerSettings()
    {
        var s = SettingsStorage.Load();
        s.Timer.ActionIsSignal = _actionIsSignal;
        if (!_actionIsSignal)
            s.Timer.DelaySeconds = _delaySeconds;
        s.Timer.ResourceTriggerKey = _resourceWatchKey;
        s.Timer.ResourceConditionKind = LegacyResourceConditionKind(_resourceWatchKey);
        s.Timer.ResourceThresholdPercent = _resourceThresholdPercent;
        SettingsStorage.Save(s);
    }

    private static string MigrateResourceTriggerKey(TimerSettings t)
    {
        if (!string.IsNullOrWhiteSpace(t.ResourceTriggerKey))
            return NormalizeResourceTriggerKey(t.ResourceTriggerKey.Trim());
        return t.ResourceConditionKind switch
        {
            1 => ResourceTriggerKeys.Gpu(0),
            2 => ResourceTriggerKeys.Internet,
            _ => ResourceTriggerKeys.Cpu
        };
    }

    private static string NormalizeResourceTriggerKey(string key)
    {
        if (string.Equals(key, ResourceTriggerKeys.Cpu, StringComparison.OrdinalIgnoreCase))
            return ResourceTriggerKeys.Cpu;
        if (string.Equals(key, ResourceTriggerKeys.Internet, StringComparison.OrdinalIgnoreCase))
            return ResourceTriggerKeys.Internet;
        if (key.StartsWith("gpu:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(4), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var slot))
        {
            return ResourceTriggerKeys.Gpu(slot);
        }

        if (key.StartsWith("net:", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
        {
            var requestedId = key.Substring(4);
            foreach (var (id, _) in ResourceMonitorSampler.DiscoverNetworkInterfaces())
            {
                if (string.Equals(id, requestedId, StringComparison.OrdinalIgnoreCase))
                    return ResourceTriggerKeys.NetInterface(id);
            }

            return ResourceTriggerKeys.NetInterface(requestedId);
        }

        return ResourceTriggerKeys.Cpu;
    }

    private static int LegacyResourceConditionKind(string key)
    {
        if (key == ResourceTriggerKeys.Cpu)
            return 0;
        if (key.StartsWith("gpu:", StringComparison.Ordinal))
            return 1;
        if (key == ResourceTriggerKeys.Internet || key.StartsWith("net:", StringComparison.Ordinal))
            return 2;
        return 0;
    }

    private void InitResourceMonitorCombos()
    {
        _suppressResourceComboEvents = true;
        try
        {
            ResourceConditionIfText.Text = LocalizationService.T("MainWindow.ResourceConditionIf");
            ResourceConditionDropsText.Text = LocalizationService.T("MainWindow.ResourceConditionDropsTo");

            SyncResourceThresholdTextFromValue();

            ResourceKindCombo.Items.Clear();
            ResourceKindCombo.Items.Add(new ComboBoxItem
                { Content = LocalizationService.T("MainWindow.ResourceCpu"), Tag = ResourceTriggerKeys.Cpu });
            ResourceKindCombo.Items.Add(new ComboBoxItem
                { Content = LocalizationService.T("MainWindow.ResourceInternet"), Tag = ResourceTriggerKeys.Internet });

            foreach (var ad in ResourceMonitorSampler.DiscoverGpuAdapters())
            {
                ResourceKindCombo.Items.Add(new ComboBoxItem
                    { Content = ad.DisplayLabel, Tag = ResourceTriggerKeys.Gpu(ad.SlotIndex) });
            }

            foreach (var (id, label) in ResourceMonitorSampler.DiscoverNetworkInterfaces())
            {
                ResourceKindCombo.Items.Add(new ComboBoxItem
                    { Content = label, Tag = ResourceTriggerKeys.NetInterface(id) });
            }

            SelectResourceByTriggerKey(_resourceWatchKey);
        }
        finally
        {
            _suppressResourceComboEvents = false;
        }
    }

    private void SyncResourceThresholdTextFromValue()
    {
        _suppressResourceThresholdEvents = true;
        try
        {
            ResourceThresholdTextBox.Text = _resourceThresholdPercent.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        finally
        {
            _suppressResourceThresholdEvents = false;
        }
    }

    private static int ParseResourceThresholdText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 10;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var v))
            return 10;
        return Math.Clamp(v, 0, 100);
    }

    private void CommitResourceThresholdFromText()
    {
        var v = ParseResourceThresholdText(ResourceThresholdTextBox.Text);
        _resourceThresholdPercent = v;
        SyncResourceThresholdTextFromValue();
        PersistTimerSettings();
    }

    private void ResourceThresholdTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressResourceThresholdEvents)
            return;
        CommitResourceThresholdFromText();
    }

    private void ResourceThresholdTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitResourceThresholdFromText();
            e.Handled = true;
        }
    }

    private void ResourceThresholdUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        _resourceThresholdPercent = Math.Clamp(_resourceThresholdPercent + 1, 0, 100);
        SyncResourceThresholdTextFromValue();
        PersistTimerSettings();
    }

    private void ResourceThresholdDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        _resourceThresholdPercent = Math.Clamp(_resourceThresholdPercent - 1, 0, 100);
        SyncResourceThresholdTextFromValue();
        PersistTimerSettings();
    }

    private void SelectResourceByTriggerKey(string key)
    {
        key = NormalizeResourceTriggerKey(key);
        foreach (ComboBoxItem item in ResourceKindCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            {
                ResourceKindCombo.SelectedItem = item;
                _resourceWatchKey = key;
                return;
            }
        }

        foreach (ComboBoxItem item in ResourceKindCombo.Items)
        {
            if (item.Tag is string t && t == ResourceTriggerKeys.Cpu)
            {
                ResourceKindCombo.SelectedItem = item;
                _resourceWatchKey = ResourceTriggerKeys.Cpu;
                return;
            }
        }
    }

    private void ApplyResourceMonitorPanelClosedUi()
    {
        ResourceMonitorOverlay.Visibility = Visibility.Collapsed;
        MainTimerStack.Visibility = Visibility.Visible;
        ResourceKindCombo.IsEnabled = true;
        ResourceThresholdTextBox.IsEnabled = true;
        ResourceThresholdUpButton.IsEnabled = true;
        ResourceThresholdDownButton.IsEnabled = true;
    }

    private void ApplyResourceMonitorWatchingUi()
    {
        MainTimerStack.Visibility = Visibility.Collapsed;
        ResourceMonitorOverlay.Visibility = Visibility.Visible;
        ResourceKindCombo.IsEnabled = false;
        ResourceThresholdTextBox.IsEnabled = false;
        ResourceThresholdUpButton.IsEnabled = false;
        ResourceThresholdDownButton.IsEnabled = false;
    }

    private void OpenResourceMonitorFromMenu()
    {
        CloseExactTimeDialog();
        InitResourceMonitorCombos();
        MainTimerStack.Visibility = Visibility.Visible;
        ResourceMonitorOverlay.Visibility = Visibility.Visible;
        _resourceSampler?.Dispose();
        _resourceSampler = new ResourceMonitorSampler();
        EnsureResourcePollTimer();
        UpdateStartEnabled();
    }

    private void CloseResourceMonitorPanel()
    {
        if (ResourceMonitorOverlay.Visibility != Visibility.Visible)
            return;

        if (_state == TimerState.ResourceWaiting)
            StopResourceConditionWatch();
        else
        {
            _resourceSampler?.Dispose();
            _resourceSampler = null;
            ReleaseResourcePollTimerIfIdle();
            ApplyResourceMonitorPanelClosedUi();
        }

        UpdateStartEnabled();
    }

    private void BtnResourceMonitorClose_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseResourceMonitorPanel();
    }

    private void ResourceKindCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressResourceComboEvents)
            return;
        if (ResourceKindCombo.SelectedItem is ComboBoxItem item && item.Tag is string k)
        {
            _resourceWatchKey = NormalizeResourceTriggerKey(k);
            PersistTimerSettings();
            _resourceSparkSamples.Clear();
            UpdateResourceSparkPolyline();
        }
    }

    private void EnsureResourcePollTimer()
    {
        if (_resourcePollTimer != null)
            return;
        _resourcePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _resourcePollTimer.Tick += OnResourcePollTick;
        _resourcePollTimer.Start();
    }

    private void ReleaseResourcePollTimerIfIdle()
    {
        if (_state == TimerState.ResourceWaiting)
            return;
        if (ResourceMonitorOverlay.Visibility == Visibility.Visible)
            return;
        if (_resourcePollTimer == null)
            return;
        _resourcePollTimer.Stop();
        _resourcePollTimer.Tick -= OnResourcePollTick;
        _resourcePollTimer = null;
    }

    private void OnResourcePollTick(object? sender, EventArgs e)
    {
        var sampler = _resourceSampler;
        if (sampler == null)
            return;
        if (_resourcePollBusy)
            return;

        _resourcePollBusy = true;

        Task.Run(() =>
        {
            ResourceSnapshot s;
            try
            {
                s = sampler.ReadSnapshot();
            }
            catch
            {
                Dispatcher.BeginInvoke(new Action(() => { _resourcePollBusy = false; }));
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_resourceSampler != sampler)
                        return;

                    if (ResourceMonitorOverlay.Visibility == Visibility.Visible)
                    {
                        UpdateResourceLiveTexts(s);
                        PushResourceSparkSample(ResourceMonitorSampler.GetPercentForKey(_resourceWatchKey, s));
                        UpdateResourceSparkPolyline();
                    }

                    if (_state != TimerState.ResourceWaiting)
                        return;

                    UpdateStatusTextForResourceWait(s);

                    var v = ResourceMonitorSampler.GetPercentForKey(_resourceWatchKey, s);
                    var thr = _resourceThresholdPercent;

                    if (!_resourceEdgePrimed)
                    {
                        _prevResourceValueForEdge = v;
                        _resourceEdgePrimed = true;
                        return;
                    }

                    if (_prevResourceValueForEdge >= thr && v < thr)
                    {
                        OnResourceConditionMet();
                        return;
                    }

                    _prevResourceValueForEdge = v;
                }
                finally
                {
                    _resourcePollBusy = false;
                }
            }), DispatcherPriority.Normal);
        });
    }

    private void ResourceMonitorOverlay_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ResourceMonitorOverlay.IsVisible)
        {
            _resourceSparkSamples.Clear();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateResourceSparkPolyline();
                TryBeginResourceSparkAnimations();
            }), DispatcherPriority.Loaded);
        }
        else
            StopResourceSparkAnimations();
    }

    private void TryBeginResourceSparkAnimations()
    {
        if (!IsLoaded || !ResourceMonitorOverlay.IsVisible)
            return;
        StopResourceSparkAnimations();
        _resourceSparkScanStoryboard ??= TryFindResource("ResourceSparkScanStoryboard") as Storyboard;
        _resourceSparkPulseStoryboard ??= TryFindResource("ResourceSparklinePulseStoryboard") as Storyboard;
        try
        {
            _resourceSparkScanStoryboard?.Begin(this, isControllable: true);
            _resourceSparkPulseStoryboard?.Begin(this, isControllable: true);
        }
        catch
        {
            // ignored
        }
    }

    private void StopResourceSparkAnimations()
    {
        try
        {
            _resourceSparkScanStoryboard?.Stop(this);
            _resourceSparkPulseStoryboard?.Stop(this);
        }
        catch
        {
            // ignored
        }
    }

    private void PushResourceSparkSample(float value)
    {
        value = (float)Math.Clamp(value, 0, 100);
        _resourceSparkSamples.Add(value);
        while (_resourceSparkSamples.Count > ResourceSparkMaxPoints)
            _resourceSparkSamples.RemoveAt(0);
    }

    private void UpdateResourceSparkPolyline()
    {
        var pts = new PointCollection();
        var n = _resourceSparkSamples.Count;
        if (n < 2)
        {
            ResourceSparkline.Points = pts;
            return;
        }

        const double pad = 2;
        var w = ResourceSparkCanvasW;
        var h = ResourceSparkCanvasH;
        for (var i = 0; i < n; i++)
        {
            var x = pad + (w - 2 * pad) * i / (n - 1);
            var y = pad + (h - 2 * pad) * (1 - _resourceSparkSamples[i] / 100.0);
            pts.Add(new System.Windows.Point(x, y));
        }

        ResourceSparkline.Points = pts;
    }

    private void UpdateResourceLiveTexts(ResourceSnapshot s)
    {
        var name = GetResourceWatchDisplayName();
        var v = ResourceMonitorSampler.GetPercentForKey(_resourceWatchKey, s);
        var pct = (int)Math.Round(Math.Clamp(v, 0, 100));
        ResourceLiveSelectedText.Text = LocalizationService.T("MainWindow.ResourceLiveSelected", name, pct);
    }

    private void UpdateStatusTextForResourceWait(ResourceSnapshot? s)
    {
        var name = GetResourceWatchDisplayName();
        var line = LocalizationService.T("MainWindow.StatusResourceWait", name, _resourceThresholdPercent);
        if (s.HasValue)
        {
            var cur = ResourceMonitorSampler.GetPercentForKey(_resourceWatchKey, s.Value);
            var cv = (int)Math.Round(Math.Clamp(cur, 0, 100));
            line += " · " + LocalizationService.T("MainWindow.StatusResourceCurrent", cv);
        }

        StatusText.Text = line;
    }

    private string GetResourceWatchDisplayName()
    {
        if (ResourceKindCombo.SelectedItem is ComboBoxItem c)
        {
            if (c.Content is string s)
                return s;
            if (c.Content != null)
                return c.Content.ToString() ?? _resourceWatchKey;
        }

        return _resourceWatchKey;
    }

    private void ApplyResourceSelectionFromUi()
    {
        if (ResourceKindCombo.SelectedItem is ComboBoxItem rki && rki.Tag is string k)
            _resourceWatchKey = NormalizeResourceTriggerKey(k);
        _resourceThresholdPercent = ParseResourceThresholdText(ResourceThresholdTextBox.Text);
    }

    private void StartResourceConditionWatch()
    {
        if (_state != TimerState.Idle)
            return;

        ApplyResourceSelectionFromUi();
        PersistTimerSettings();

        _resourceWatchH = HoursDigit.Value;
        _resourceWatchM = MinutesDigit.Value;
        _resourceWatchS = SecondsDigit.Value;

        _state = TimerState.ResourceWaiting;

        _resourceSampler?.Dispose();
        _resourceSampler = new ResourceMonitorSampler();
        _resourceEdgePrimed = false;
        _prevResourceValueForEdge = 0;

        ApplyResourceMonitorWatchingUi();

        SetDigitsReadOnly(true);
        SetOptionsEnabled(false);
        SetMainTimerDigitsZero();
        UpdateStatusTextForResourceWait(null);
        StartColonBlink();

        EnsureResourcePollTimer();
        UpdateButtonState();
        SyncTrayCountdownIcon();
    }

    private void StopResourceConditionWatch()
    {
        if (_resourcePollTimer != null)
        {
            _resourcePollTimer.Stop();
            _resourcePollTimer.Tick -= OnResourcePollTick;
            _resourcePollTimer = null;
        }

        _resourceSampler?.Dispose();
        _resourceSampler = null;

        _state = TimerState.Idle;
        HoursDigit.SetValueSilent(_resourceWatchH);
        MinutesDigit.SetValueSilent(_resourceWatchM);
        SecondsDigit.SetValueSilent(_resourceWatchS);
        RecalcRemaining();

        ApplyResourceMonitorPanelClosedUi();

        SetDigitsReadOnly(false);
        SetOptionsEnabled(true);
        StopColonBlink();
        StatusText.Text = "";
        UpdateButtonState();
        SyncTrayCountdownIcon();
        UpdateStartEnabled();
    }

    private void OnResourceConditionMet()
    {
        if (_resourcePollTimer != null)
        {
            _resourcePollTimer.Stop();
            _resourcePollTimer.Tick -= OnResourcePollTick;
            _resourcePollTimer = null;
        }

        _resourceSampler?.Dispose();
        _resourceSampler = null;

        ApplyResourceMonitorPanelClosedUi();

        StatusText.Text = "";

        int delaySec = GetDelaySeconds();
        if (delaySec > 0)
        {
            PulseColonForOneSecond();
            StartDelayCountdown(delaySec);
        }
        else
        {
            ExecuteAction();
        }
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

        if (_state == TimerState.Running || _state == TimerState.ResourceWaiting)
            StartColonBlink();
        else
            StopColonBlink();
    }

    private void ApplyExactTimeDigitTheme()
    {
        bool shutdown = !_actionIsSignal;
        ExactTimeHours.ShutdownAction = shutdown;
        ExactTimeMinutes.ShutdownAction = shutdown;
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

    private int GetDelaySeconds() => _actionIsSignal ? 0 : _delaySeconds;

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
        ResourceMonitorOverlay.IsVisibleChanged -= ResourceMonitorOverlay_OnIsVisibleChanged;
        StopResourceSparkAnimations();
        _closeActionPopupTimer?.Stop();
        _closeDelayPopupTimer?.Stop();
        if (_resourcePollTimer != null)
        {
            _resourcePollTimer.Stop();
            _resourcePollTimer.Tick -= OnResourcePollTick;
            _resourcePollTimer = null;
        }

        _resourcePollBusy = false;
        _resourceSampler?.Dispose();
        _resourceSampler = null;
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
        _actionIsSignal = false;
        ActionPillText.Text = LocalizationService.T("MainWindow.ActionShutdown");
        _delaySeconds = SettingsStorage.Load().Timer.DelaySeconds;
        DelayPillText.Text = GetDelayLabelText(_delaySeconds);
        CancelCloseActionPopup();
        ActionPopup.IsOpen = false;
        ApplyActionOptionCardVisuals();
        ApplyActionVisualTheme();
        UpdateDelayOptionCardVisibility();
        PersistTimerSettings();
        SyncTrayCountdownIcon();
    }

    private void ActionPickSignal_OnClick(object sender, RoutedEventArgs e)
    {
        _actionIsSignal = true;
        ActionPillText.Text = LocalizationService.T("MainWindow.ActionSignal");
        _delaySeconds = 0;
        DelayPillText.Text = GetDelayLabelText(0);
        CancelCloseActionPopup();
        ActionPopup.IsOpen = false;
        ApplyActionOptionCardVisuals();
        ApplyActionVisualTheme();
        UpdateDelayOptionCardVisibility();
        PersistTimerSettings();
        SyncTrayCountdownIcon();
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
            _countdown?.Stop();
            _countdown = null;
            CloseDelayOverlay();
            ExecuteAction();
            return;
        }

        _remainingSeconds = sec;
        UpdateTimerDisplay();
        UpdateStatusText();
        SyncDelayOverlayCountdown();
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
        if (ResourceMonitorOverlay.Visibility == Visibility.Visible)
        {
            BtnStartStop.IsEnabled = true;
            return;
        }

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
        SyncTrayCountdownIcon();
    }

    private void StopTimer()
    {
        if (_state == TimerState.ResourceWaiting)
        {
            StopResourceConditionWatch();
            return;
        }

        bool wasDelay = _state == TimerState.DelayCountdown;
        _state = TimerState.Idle;
        _countdown?.Stop();
        _countdown = null;
        CloseDelayOverlay();
        SetDigitsReadOnly(false);
        SetOptionsEnabled(true);
        UpdateButtonState();
        UpdateStatusText();
        StopColonBlink();
        UpdateStartEnabled();
        SyncTrayCountdownIcon();
    }

    private void ShowDelayOverlay()
    {
        // Сначала главное окно (в т.ч. из трея), затем оверлей — иначе отдельное окно отсрочки активирует процесс до показа main.
        _suppressEnsureDelayOverlayAfterShowFromTray = true;
        try
        {
            ShowFromTray();
            AttachDelayOverlayWindow();
        }
        finally
        {
            _suppressEnsureDelayOverlayAfterShowFromTray = false;
        }
    }

    private void AttachDelayOverlayWindow()
    {
        CloseDelayOverlay();
        var w = new DelayOverlayWindow();
        w.SetActionMode(_actionIsSignal);
        w.SetRemainingSeconds(_remainingSeconds);
        w.CancelRequested += DelayOverlay_OnCancelRequested;
        w.ConfirmNowRequested += DelayOverlay_OnConfirmNowRequested;
        w.Show();
        _delayOverlay = w;
        SetDelayPhaseMainDimmer(true);
    }

    /// <summary>Если отсрочка шла без оверлея (окно было в фоне), показываем оверлей после ручного открытия окна.</summary>
    private void EnsureDelayOverlayAttached()
    {
        if (_state != TimerState.DelayCountdown || _delayOverlay != null)
            return;
        if (!IsVisible)
            return;
        AttachDelayOverlayWindow();
    }

    private void FinishShowFromTrayIfNeeded()
    {
        if (_suppressEnsureDelayOverlayAfterShowFromTray)
            return;
        EnsureDelayOverlayAttached();
    }

    private void CloseDelayOverlay()
    {
        SetDelayPhaseMainDimmer(false);
        if (_delayOverlay == null)
            return;
        _delayOverlay.CancelRequested -= DelayOverlay_OnCancelRequested;
        _delayOverlay.ConfirmNowRequested -= DelayOverlay_OnConfirmNowRequested;
        _delayOverlay.Close();
        _delayOverlay = null;
    }

    private void SetDelayPhaseMainDimmer(bool active, bool stealFocusWithDimmer = true)
    {
        DelayPhaseMainDimmer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active || !stealFocusWithDimmer)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { DelayPhaseMainDimmer.Focus(); }
            catch { /* ignored */ }
        }), DispatcherPriority.Input);
    }

    private void SyncDelayOverlayCountdown()
    {
        if (_state == TimerState.DelayCountdown)
            _delayOverlay?.SetRemainingSeconds(_remainingSeconds);
    }

    private void DelayOverlay_OnCancelRequested(object? sender, EventArgs e)
    {
        if (_delayOverlay != null && ReferenceEquals(sender, _delayOverlay))
            StopTimer();
    }

    private void DelayOverlay_OnConfirmNowRequested(object? sender, EventArgs e)
    {
        if (_delayOverlay != null && ReferenceEquals(sender, _delayOverlay))
            DelayOverlayConfirmNow();
    }

    private void DelayOverlayConfirmNow()
    {
        if (_state != TimerState.DelayCountdown)
            return;

        _countdown?.Stop();
        _countdown = null;
        _remainingSeconds = 0;
        UpdateTimerDisplay();
        CloseDelayOverlay();

        ExecuteAction();
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
                PulseColonForOneSecond();
                StartDelayCountdown(delaySec);
            }
            else
            {
                ExecuteAction();
            }
            return;
        }

        UpdateTimerDisplay();
        PulseColonForOneSecond();
    }

    private void StartDelayCountdown(int delaySec)
    {
        _remainingSeconds = delaySec;
        _state = TimerState.DelayCountdown;

        // Главный таймер остаётся на нуле; отсчёт отсрочки только в оверлее
        UpdateTimerDisplay();
        UpdateButtonState();
        UpdateStatusText();
        StopColonBlink();

        ShowDelayOverlay();

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
            _countdown?.Stop();
            _countdown = null;
            CloseDelayOverlay();
            ExecuteAction();
            return;
        }

        SyncDelayOverlayCountdown();
    }

    private void ExecuteAction()
    {
        ShowFromTray();
        switch (SelectedAction)
        {
            case ActionKind.Shutdown:
                InitiateImmediateSystemShutdown();
                _state = TimerState.Idle;
                SetDigitsReadOnly(false);
                SetOptionsEnabled(true);
                UpdateButtonState();
                StopColonBlink();
                UpdateStartEnabled();
                StatusText.Text = "";
                SyncTrayCountdownIcon();
                break;

            case ActionKind.Signal:
                ShowSignalFinishedOverlay();
                SyncTrayCountdownIcon();
                break;
        }
    }

    private void ShowSignalFinishedOverlay()
    {
        PlayAlarmSignal();
        SetDelayPhaseMainDimmer(true, stealFocusWithDimmer: false);
        SignalFinishedOverlay.Visibility = Visibility.Visible;
        _signalEqualizerStoryboard ??= TryFindResource("SignalEqualizerStoryboard") as Storyboard;
        _signalEqualizerStoryboard?.Begin(SignalFinishedOverlay, isControllable: true);
        _state = TimerState.SignalFinished;
        SetDigitsReadOnly(true);
        SetOptionsEnabled(false);
        UpdateButtonState();
        StopColonBlink();
        StatusText.Text = "";
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { BtnSignalFinishedOk.Focus(); }
            catch { /* ignored */ }
        }), DispatcherPriority.Input);
    }

    private void DismissSignalFinishedOverlay()
    {
        if (_state != TimerState.SignalFinished)
            return;
        lock (SignalPlaybackLock)
            DisposeSignalPlayback();
        _signalEqualizerStoryboard?.Stop(SignalFinishedOverlay);
        SetDelayPhaseMainDimmer(false);
        SignalFinishedOverlay.Visibility = Visibility.Collapsed;
        _state = TimerState.Idle;
        SetDigitsReadOnly(false);
        SetOptionsEnabled(true);
        UpdateButtonState();
        UpdateStartEnabled();
        SyncTrayCountdownIcon();
    }

    private void BtnSignalFinishedOk_OnClick(object sender, RoutedEventArgs e) => DismissSignalFinishedOverlay();

    private void MainWindow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right) return;
        if (_state != TimerState.Idle) return;
        if (ExactTimeOverlay.Visibility == Visibility.Visible) return;
        if (ResourceMonitorOverlay.Visibility == Visibility.Visible) return;

        // ПКМ по любой кнопке — не показывать «точное время» (на «Старт» — сброс, как раньше; на неактивной «Старт» — тишина)
        if (ShouldSuppressExactTimeContextMenu(e))
            return;

        OpenExactTimeContextMenu();
        e.Handled = true;
    }

    private bool ShouldSuppressExactTimeContextMenu(MouseButtonEventArgs e)
    {
        if (IsDescendantOfButton(e.OriginalSource as DependencyObject))
            return true;
        // Отключённая кнопка не в цепочке hit-test — клик уходит под неё, но координаты всё ещё над «Старт»
        if (IsMouseOverElement(BtnStartStop, e))
            return true;
        return false;
    }

    private static bool IsDescendantOfButton(DependencyObject? source)
    {
        for (var o = source; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (o is WpfButton)
                return true;
        }
        return false;
    }

    private static bool IsMouseOverElement(FrameworkElement el, MouseButtonEventArgs e)
    {
        if (el == null || !el.IsVisible)
            return false;
        var w = el.ActualWidth;
        var h = el.ActualHeight;
        if (w <= 0 || h <= 0)
            return false;
        var pt = e.GetPosition(el);
        return pt.X >= 0 && pt.Y >= 0 && pt.X <= w && pt.Y <= h;
    }

    private void OpenExactTimeContextMenu()
    {
        var cm = new ContextMenu();
        var item = new MenuItem();
        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var clockIcon = new TextBlock
        {
            Text = "\uE823",
            FontFamily = Application.Current.TryFindResource("Rs.Font.Mdl2") as System.Windows.Media.FontFamily
                         ?? new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/segmdl2.ttf#Segoe MDL2 Assets"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        clockIcon.SetResourceReference(TextBlock.ForegroundProperty, "Rs.Text.Primary");
        var label = new TextBlock
        {
            Text = LocalizationService.T("MainWindow.ExactTimeMenu"),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Rs.Text.Primary");
        header.Children.Add(clockIcon);
        header.Children.Add(label);
        item.Header = header;
        item.Click += (_, _) => OpenExactTimeDialog();
        cm.Items.Add(item);

        var itemRes = new MenuItem();
        var headerRes = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var monitorIcon = new TextBlock
        {
            Text = "\uE9D2",
            FontFamily = Application.Current.TryFindResource("Rs.Font.Mdl2") as System.Windows.Media.FontFamily
                         ?? new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/segmdl2.ttf#Segoe MDL2 Assets"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        monitorIcon.SetResourceReference(TextBlock.ForegroundProperty, "Rs.Text.Primary");
        var labelRes = new TextBlock
        {
            Text = LocalizationService.T("MainWindow.ResourceMonitorMenu"),
            VerticalAlignment = VerticalAlignment.Center
        };
        labelRes.SetResourceReference(TextBlock.ForegroundProperty, "Rs.Text.Primary");
        headerRes.Children.Add(monitorIcon);
        headerRes.Children.Add(labelRes);
        itemRes.Header = headerRes;
        itemRes.Click += (_, _) => OpenResourceMonitorFromMenu();
        cm.Items.Add(itemRes);

        cm.PlacementTarget = this;
        cm.Placement = PlacementMode.MousePoint;
        cm.IsOpen = true;
    }

    private void OpenExactTimeDialog()
    {
        CloseResourceMonitorPanel();
        var n = DateTime.Now;
        ExactTimeHours.SetValueSilent(n.Hour);
        ExactTimeMinutes.SetValueSilent(n.Minute);
        ApplyExactTimeDigitTheme();
        ExactTimeOverlay.Visibility = Visibility.Visible;
        SetDelayPhaseMainDimmer(true, stealFocusWithDimmer: false);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { BtnExactTimeStart.Focus(); }
            catch { /* ignored */ }
        }), DispatcherPriority.Input);
    }

    private void CloseExactTimeDialog()
    {
        if (ExactTimeOverlay.Visibility != Visibility.Visible)
            return;
        ExactTimeOverlay.Visibility = Visibility.Collapsed;
        SetDelayPhaseMainDimmer(false);
    }

    private void BtnExactTimeClose_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseExactTimeDialog();
    }

    private void BtnExactTimeStart_OnClick(object sender, RoutedEventArgs e)
    {
        int h = ExactTimeHours.Value;
        int m = ExactTimeMinutes.Value;
        var now = DateTime.Now;
        var target = new DateTime(now.Year, now.Month, now.Day, h, m, 0, DateTimeKind.Local);
        if (target <= now)
            target = target.AddDays(1);

        var secs = (int)(target - now).TotalSeconds;
        if (secs <= 0)
            return;

        CloseExactTimeDialog();

        HoursDigit.SetValueSilent(secs / 3600);
        MinutesDigit.SetValueSilent((secs % 3600) / 60);
        SecondsDigit.SetValueSilent(secs % 60);
        StartTimer();
    }

    private void ExactTimeOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsDescendantOf(e.OriginalSource as DependencyObject, ExactTimeDialogRoot))
            return;
        e.Handled = true;
        BeginDragMoveWindow();
    }

    private void SyncTrayCountdownIcon()
    {
        if (Application.Current is not App app)
            return;
        bool active = _state == TimerState.Running
                      || _state == TimerState.DelayCountdown
                      || _state == TimerState.ResourceWaiting;
        bool resWait = _state == TimerState.ResourceWaiting;
        app.SyncTrayCountdownIcon(active, _remainingSeconds, _actionIsSignal, resWait);
    }

    private void UpdateTimerDisplay()
    {
        if (_state == TimerState.DelayCountdown || _state == TimerState.ResourceWaiting)
        {
            SetMainTimerDigitsZero();
            SyncTrayCountdownIcon();
            return;
        }

        int h = _remainingSeconds / 3600;
        int m = (_remainingSeconds % 3600) / 60;
        int s = _remainingSeconds % 60;

        if (h != HoursDigit.Value) HoursDigit.AnimateToValue(h, slideDown: true);
        if (m != MinutesDigit.Value) MinutesDigit.AnimateToValue(m, slideDown: true);
        if (s != SecondsDigit.Value) SecondsDigit.AnimateToValue(s, slideDown: true);
        SyncTrayCountdownIcon();
    }

    private void SetMainTimerDigitsZero()
    {
        if (HoursDigit.Value != 0) HoursDigit.AnimateToValue(0, slideDown: true);
        if (MinutesDigit.Value != 0) MinutesDigit.AnimateToValue(0, slideDown: true);
        if (SecondsDigit.Value != 0) SecondsDigit.AnimateToValue(0, slideDown: true);
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
    /// <summary>Мгновенное выключение через Windows (/f — принудительное закрытие приложений). Отсчёт до выключения — только в приложении.</summary>
    private static void InitiateImmediateSystemShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /f /t 0")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    private static readonly Uri SignalSoundPackUri =
        new("pack://application:,,,/Assets/sound-03-minecraft.mp3", UriKind.Absolute);

    private static void PlayAlarmSignal()
    {
        lock (SignalPlaybackLock)
        {
            try
            {
                DisposeSignalPlayback();

                var sri = Application.GetResourceStream(SignalSoundPackUri);
                if (sri?.Stream == null)
                    return;

                using (sri.Stream)
                {
                    _signalMp3Memory = new MemoryStream();
                    sri.Stream.CopyTo(_signalMp3Memory);
                }

                _signalMp3Memory.Position = 0;
                _signalMp3Reader = new Mp3FileReader(_signalMp3Memory);

                var waveOut = new WaveOutEvent();
                _signalWaveOut = waveOut;
                waveOut.Init(_signalMp3Reader);
                waveOut.PlaybackStopped += SignalWaveOut_OnPlaybackStopped;
                waveOut.Play();
            }
            catch
            {
                DisposeSignalPlayback();
            }
        }
    }

    private static void SignalWaveOut_OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (SignalPlaybackLock)
            DisposeSignalPlayback();
    }

    private static void DisposeSignalPlayback()
    {
        if (_signalWaveOut != null)
        {
            try
            {
                _signalWaveOut.PlaybackStopped -= SignalWaveOut_OnPlaybackStopped;
                _signalWaveOut.Stop();
            }
            catch { /* ignored */ }

            try { _signalWaveOut.Dispose(); } catch { /* ignored */ }
            _signalWaveOut = null;
        }

        try { _signalMp3Reader?.Dispose(); } catch { /* ignored */ }
        _signalMp3Reader = null;
        _signalMp3Memory = null;
    }

    // ── UI state ────────────────────────────────────────────

    private void UpdateButtonState()
    {
        bool showStop = _state == TimerState.Running
                        || _state == TimerState.DelayCountdown
                        || _state == TimerState.ResourceWaiting;
        bool shouldAnimate = _wasShowingStopButton.HasValue && _wasShowingStopButton.Value != showStop;

        void ApplyStartStopLabelAndEnabled()
        {
            switch (_state)
            {
                case TimerState.Idle:
                    BtnStartStop.Content = LocalizationService.T("MainWindow.BtnStart");
                    break;
                case TimerState.Running:
                case TimerState.DelayCountdown:
                case TimerState.ResourceWaiting:
                    BtnStartStop.Content = LocalizationService.T("MainWindow.BtnStop");
                    BtnStartStop.IsEnabled = true;
                    break;
                case TimerState.SignalFinished:
                    BtnStartStop.Content = LocalizationService.T("MainWindow.BtnStart");
                    BtnStartStop.IsEnabled = false;
                    break;
            }

            if (_state == TimerState.Idle)
                UpdateStartEnabled();
        }

        if (shouldAnimate)
            PlayStartStopButtonTransitionAnimation(ApplyStartStopLabelAndEnabled);
        else
            ApplyStartStopLabelAndEnabled();

        _wasShowingStopButton = showStop;
    }

    private void PlayStartStopButtonTransitionAnimation(Action applyNewLabel)
    {
        BtnStartStop.BeginAnimation(UIElement.OpacityProperty, null);
        if (BtnStartStop.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        BtnStartStop.Opacity = 1;

        if (BtnStartStop.RenderTransform is not ScaleTransform st)
        {
            applyNewLabel();
            return;
        }

        var durIn = TimeSpan.FromMilliseconds(85);
        var durOut = TimeSpan.FromMilliseconds(165);

        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.38 };

        var shrinkX = new DoubleAnimation(1.0, 0.84, durIn) { EasingFunction = easeIn };
        var shrinkY = new DoubleAnimation(1.0, 0.88, durIn) { EasingFunction = easeIn };
        var fadeOut = new DoubleAnimation(1.0, 0.55, durIn) { EasingFunction = easeIn };

        shrinkX.Completed += (_, _) =>
        {
            applyNewLabel();

            var growX = new DoubleAnimation(0.84, 1.0, durOut) { EasingFunction = easeOut };
            var growY = new DoubleAnimation(0.88, 1.0, durOut) { EasingFunction = easeOut };
            var fadeIn = new DoubleAnimation(0.55, 1.0, durOut) { EasingFunction = easeOut };

            st.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, growY);
            BtnStartStop.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        st.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
        BtnStartStop.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void UpdateStatusText()
    {
        if (_state == TimerState.ResourceWaiting)
            return;
        StatusText.Text = "";
    }

    // ── Colon blink animation ───────────────────────────────
    // Один полный цикл (ярко → тускло → ярко) длится ровно 1 с и запускается в том же тике, что и смена секунды
    // (см. PulseColonForOneSecond в OnCountdownTick / OnDelayTick). Бесконечная анимация не используется — она расходилась по фазе с таймером.

    private void StartColonBlink()
    {
        Colon1.BeginAnimation(OpacityProperty, null);
        Colon2.BeginAnimation(OpacityProperty, null);

        var colonBrush = !_actionIsSignal
            ? (Brush)FindResource("Rs.Danger")
            : (Brush)FindResource("Rs.Text.Primary");
        Colon1.Foreground = colonBrush;
        Colon2.Foreground = colonBrush;
        Colon1.Opacity = 1.0;
        Colon2.Opacity = 1.0;
    }

    private void PulseColonForOneSecond()
    {
        Colon1.BeginAnimation(OpacityProperty, null);
        Colon2.BeginAnimation(OpacityProperty, null);
        Colon1.Opacity = 1.0;
        Colon2.Opacity = 1.0;

        var dur = TimeSpan.FromMilliseconds(ColonPulseHalfDurationMs);
        var a1 = new DoubleAnimation(1.0, 0.15, dur) { AutoReverse = true };
        var a2 = new DoubleAnimation(1.0, 0.15, dur) { AutoReverse = true };
        Colon1.BeginAnimation(OpacityProperty, a1);
        Colon2.BeginAnimation(OpacityProperty, a2);
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
                if (ResourceMonitorOverlay.Visibility == Visibility.Visible)
                    StartResourceConditionWatch();
                else
                    StartTimer();
                break;
            case TimerState.Running:
            case TimerState.DelayCountdown:
            case TimerState.ResourceWaiting:
                StopTimer();
                break;
            case TimerState.SignalFinished:
                break;
        }
    }

    private void BtnStartStop_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_state == TimerState.SignalFinished)
            return;

        if (_state == TimerState.Running || _state == TimerState.DelayCountdown || _state == TimerState.ResourceWaiting)
            StopTimer();

        HoursDigit.SetValueSilent(0);
        MinutesDigit.SetValueSilent(0);
        SecondsDigit.SetValueSilent(0);
        RecalcRemaining();
        UpdateStartEnabled();
        PersistTimerSettings();
    }

    private void TitleBar_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            BeginDragMoveWindow();
    }

    /// <summary>Перетаскивание безрамочного окна с произвольной области: <see cref="DragMove"/> с <see cref="WindowChrome"/> часто не срабатывает.</summary>
    private void BeginDragMoveWindow()
    {
        try
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            if (h == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(h, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }
        catch
        {
            // окно может закрываться
        }
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        if (node == null || ancestor == null) return false;
        for (var o = node; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (ReferenceEquals(o, ancestor)) return true;
        }
        return false;
    }

    private void SignalFinishedOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsDescendantOf(e.OriginalSource as DependencyObject, BtnSignalFinishedOk))
            return;
        e.Handled = true;
        BeginDragMoveWindow();
    }

    private void DelayPhaseMainDimmerDark_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        BeginDragMoveWindow();
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
            FinishShowFromTrayIfNeeded();
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
            FinishShowFromTrayIfNeeded();
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
        FinishShowFromTrayIfNeeded();
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
        if (ExactTimeOverlay.Visibility == Visibility.Visible)
            CloseExactTimeDialog();
        if (ResourceMonitorOverlay.Visibility == Visibility.Visible)
            CloseResourceMonitorPanel();

        // Сначала подтверждение при работающем таймере — и при закрытии крестиком, и при «Выход» из трея
        // (раньше App.ShutdownRequested обрабатывался раньше и пропускал этот шаг).
        if (_state == TimerState.Running || _state == TimerState.DelayCountdown || _state == TimerState.ResourceWaiting)
        {
            var dlg = new ConfirmCloseWindow { Owner = this };
            if (!IsVisible)
            {
                dlg.Owner = null;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (dlg.ShowDialog() != true)
            {
                e.Cancel = true;
                App.ShutdownRequested = false;
                return;
            }

            StopTimer();
        }

        if (_state == TimerState.SignalFinished)
            DismissSignalFinishedOverlay();

        if (App.ShutdownRequested)
        {
            _countdown?.Stop();
            _countdown = null;
            CloseDelayOverlay();
            base.OnClosing(e);
            return;
        }

        var s = SettingsStorage.Load();
        if (s.Window.MinimizeToTrayOnCloseClick)
        {
            e.Cancel = true;
            HideToTrayCore(s);
            return;
        }

        _countdown?.Stop();
        _countdown = null;
        CloseDelayOverlay();
        base.OnClosing(e);
    }

    #region TEMP — удалить после проверки окна отсрочки (клавиша Q)

    private void MainWindow_PreviewKeyDown_TempDelayOverlayTest(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ExactTimeOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseExactTimeDialog();
            return;
        }

        if (ResourceMonitorOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseResourceMonitorPanel();
            return;
        }

        if (e.Key != Key.Q)
            return;
        if (_state == TimerState.SignalFinished)
            return;
        e.Handled = true;
        OpenDelayOverlayTestPreview();
    }

    /// <summary>Независимое окно для визуальной проверки вёрстки; не трогает таймер.</summary>
    private void OpenDelayOverlayTestPreview()
    {
        var w = new DelayOverlayWindow
        {
            Owner = this
        };
        w.SetActionMode(_actionIsSignal);
        w.SetRemainingSeconds(3725); // 1:02:05 — проверка формата с часами
        w.Closed += (_, _) =>
        {
            if (_delayOverlay == null)
                SetDelayPhaseMainDimmer(false);
        };
        w.CancelRequested += (_, _) => w.Close();
        w.ConfirmNowRequested += (_, _) => w.Close();
        SetDelayPhaseMainDimmer(true);
        w.Show();
    }

    #endregion
}
