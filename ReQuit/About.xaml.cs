using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ReQuit.Services;
using WpfButton = System.Windows.Controls.Button;

namespace ReQuit;

public partial class About
{
    public About()
    {
        InitializeComponent();
    }

    private void About_OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionValue.Text = " " + ReUpdate.ReUpdateConfig.CurrentVersion.ToString(2);
        CheckForUpdatesCheckBox.IsChecked = SettingsStorage.Load().Ui.CheckForUpdates;
        UpdateWhatsNewDot();
        ReUpdate.WhatsNewState.Changed += UpdateWhatsNewDot;
        Closed += (_, _) => ReUpdate.WhatsNewState.Changed -= UpdateWhatsNewDot;

        try
        {
            var uri = new Uri("pack://application:,,,/app.ico");
            var decoder = BitmapDecoder.Create(
                uri,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapFrame? best = null;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (best == null || frame.PixelWidth > best.PixelWidth)
                    best = frame;
            }

            if (best == null)
                return;

            if (best.CanFreeze)
                best.Freeze();

            TitleBarIcon.Source = best;
            RenderOptions.SetBitmapScalingMode(TitleBarIcon, BitmapScalingMode.Fant);
        }
        catch
        {
        }
    }

    private void UpdateWhatsNewDot() =>
        WhatsNewDotAbout.Visibility = ReUpdate.WhatsNewState.HasUnseen ? Visibility.Visible : Visibility.Collapsed;

    private void WhatsNew_OnClick(object sender, RoutedEventArgs e) => ReUpdate.WhatsNewWindow.ShowFor(this);

    private void CheckForUpdatesCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStorage.Load();
        settings.Ui.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        SettingsStorage.Save(settings);
    }

    private void SocialLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string url })
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void DonateLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    public static void Show(Window? owner)
    {
        try
        {
            var w = new About();
            if (owner is { IsLoaded: true, IsVisible: true })
                w.Owner = owner;
            else
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
        }
        catch
        {
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void TitleBarClose_OnClick(object sender, RoutedEventArgs e) => Close();
}
