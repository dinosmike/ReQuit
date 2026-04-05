using System;
using System.Windows;
using System.Windows.Input;
using ReQuit.Services;

namespace ReQuit;

public partial class DelayOverlayWindow : Window
{
    private bool _actionIsSignal;

    public event EventHandler? CancelRequested;
    public event EventHandler? ConfirmNowRequested;

    public DelayOverlayWindow()
    {
        InitializeComponent();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= OnLanguageChanged;
        KeyDown += DelayOverlayWindow_OnKeyDown;
        RefreshLocalizedUi();
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(RefreshLocalizedUi);

    public void SetActionMode(bool actionIsSignal)
    {
        _actionIsSignal = actionIsSignal;
        RefreshMessageAndConfirmLabel();
    }

    public void SetRemainingSeconds(int totalSeconds)
    {
        CountdownBlock.Text = FormatCountdown(totalSeconds);
    }

    private void RefreshLocalizedUi()
    {
        TitleBlock.Text = LocalizationService.T("DelayOverlay.Title");
        RefreshMessageAndConfirmLabel();
    }

    private void RefreshMessageAndConfirmLabel()
    {
        MessageBlock.Text = _actionIsSignal
            ? LocalizationService.T("DelayOverlay.MessageSignal")
            : LocalizationService.T("DelayOverlay.MessageShutdown");
        BtnCancel.Content = _actionIsSignal
            ? LocalizationService.T("DelayOverlay.BtnCancel")
            : LocalizationService.T("DelayOverlay.BtnCancelShutdown");
        BtnConfirmNow.Content = _actionIsSignal
            ? LocalizationService.T("DelayOverlay.BtnSignalNow")
            : LocalizationService.T("DelayOverlay.BtnShutdownNow");
    }

    private static string FormatCountdown(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        if (h > 0)
            return $"{h:D2}:{m:D2}:{s:D2}";
        return $"{m:D2}:{s:D2}";
    }

    private void DragSurface_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnCancel_OnClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void BtnConfirmNow_OnClick(object sender, RoutedEventArgs e) => ConfirmNowRequested?.Invoke(this, EventArgs.Empty);

    private void DelayOverlayWindow_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
