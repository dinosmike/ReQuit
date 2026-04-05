using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
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
