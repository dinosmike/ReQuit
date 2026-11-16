using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using HAlign = System.Windows.HorizontalAlignment;
using VAlign = System.Windows.VerticalAlignment;

namespace ReUpdate;

/// <summary>
/// Окно «Что нового»: версии с датой и нумерованным списком, внизу переключатель
/// «Вся история» / «Только новое». Собрано кодом, без XAML, чтобы переносилось одним файлом;
/// цвета — из <see cref="ReUpdateConfig.Colors"/>.
/// </summary>
public sealed class WhatsNewWindow : Window
{
    private readonly StackPanel _list = new();
    private readonly TextBlock _toggle = new();
    private readonly Version? _seenBefore;
    private readonly List<ChangelogVersion> _versions;
    private bool _showAll;

    private static string T(string ru, string en) => ReUpdateConfig.IsRussian ? ru : en;

    /// <summary>Открыть окно и отметить новое как прочитанное.</summary>
    public static void ShowFor(Window? owner)
    {
        var w = new WhatsNewWindow { Owner = owner };
        if (owner == null || !owner.IsVisible)
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Show();
    }

    private WhatsNewWindow()
    {
        _seenBefore = WhatsNewState.LastSeen;
        var c = ReUpdateConfig.Colors;

        Title = T("Что нового", "What's new");
        Width = 460;
        // Высота по содержимому: короткий список — маленькое окно, длинная история — прокрутка.
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush(c.Surface);
        BorderBrush = Brush(c.Border);
        BorderThickness = new Thickness(1);
        UseLayoutRounding = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var title = new TextBlock
        {
            Text = Title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(c.Text),
            VerticalAlignment = VAlign.Center,
        };
        var close = new Button
        {
            Content = "×",
            FontSize = 20,
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(c.TextMuted),
            Cursor = Cursors.Hand,
            ToolTip = T("Закрыть", "Close"),
            HorizontalAlignment = HAlign.Right,
            Template = FlatButtonTemplate(),
        };
        close.Click += (_, _) => Close();
        var header = new Grid { Margin = new Thickness(22, 14, 12, 6), Background = Brushes.Transparent };
        header.Children.Add(title);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(22, 4, 16, 8),
        };
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), ScrollBarStyle(c));

        _toggle.Foreground = Brush(c.TextMuted);
        _toggle.FontSize = 13;
        _toggle.Cursor = Cursors.Hand;
        _toggle.TextDecorations = TextDecorations.Underline;
        _toggle.HorizontalAlignment = HAlign.Center;
        _toggle.Margin = new Thickness(0, 10, 0, 16);
        _toggle.MouseEnter += (_, _) => _toggle.Foreground = Brush(c.Accent);
        _toggle.MouseLeave += (_, _) => _toggle.Foreground = Brush(c.TextMuted);
        _toggle.MouseLeftButtonUp += (_, _) => { _showAll = !_showAll; Render(); };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_toggle, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_toggle);
        root.Children.Add(scroll);
        Content = root;

        // История вшита в exe: окно появляется сразу готовым.
        _versions = Changelog.Load();
        Render();
        Loaded += (_, _) => WhatsNewState.MarkSeen();
    }

    private void Render()
    {
        var c = ReUpdateConfig.Colors;
        _list.Children.Clear();

        var fresh = _versions.Where(v => _seenBefore != null && v.Parsed > _seenBefore && v.Parsed <= ReUpdateConfig.CurrentVersion).ToList();
        if (fresh.Count == 0)
            fresh = _versions.Take(1).ToList();
        var shown = _showAll ? _versions : fresh;
        // Переключатель нужен, только если в истории есть что-то кроме уже показанного.
        _toggle.Visibility = _versions.Count > fresh.Count ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Text = _showAll ? T("Только новое", "Only new") : T("Вся история", "Full history");

        foreach (var v in shown)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, _list.Children.Count == 0 ? 4 : 18, 0, 8) };
            head.Children.Add(new TextBlock
            {
                Text = v.Version,
                FontSize = 16,
                Foreground = Brush(c.Accent),
                VerticalAlignment = VAlign.Center,
            });
            var date = FormatDate(v.Date);
            if (date != null)
            {
                head.Children.Add(new Border
                {
                    Background = Brush(c.BadgeBackground),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 3),
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VAlign.Center,
                    Child = new TextBlock { Text = date, FontSize = 11, Foreground = Brush(c.Accent) },
                });
            }
            _list.Children.Add(head);

            var n = 0;
            foreach (var item in v.Items)
            {
                n++;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var num = new TextBlock { Text = n + ".", FontSize = 14, Foreground = Brush(c.TextMuted) };
                var text = new TextBlock { Text = item, FontSize = 14, Foreground = Brush(c.Text), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(text, 1);
                row.Children.Add(num);
                row.Children.Add(text);
                _list.Children.Add(row);
            }
        }
    }

    private static string? FormatDate(string iso)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;
        var culture = CultureInfo.GetCultureInfo(ReUpdateConfig.IsRussian ? "ru-RU" : "en-US");
        return ReUpdateConfig.IsRussian ? d.ToString("d MMMM yyyy", culture) : d.ToString("MMMM d, yyyy", culture);
    }

    private static SolidColorBrush Brush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    /// <summary>Тонкая полоса прокрутки: бирюзовый ползунок на тёмной дорожке, без стрелок.</summary>
    private static Style ScrollBarStyle(ReUpdateColors c)
    {
        static string Hex(Color x) => $"#{x.R:X2}{x.G:X2}{x.B:X2}";
        string track = Hex(c.ScrollTrack), thumb = Hex(c.Accent);
        var xaml = $@"
            <Style TargetType='ScrollBar'
                   xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                   xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
              <Setter Property='Width' Value='4'/>
              <Setter Property='MinWidth' Value='4'/>
              <Setter Property='Margin' Value='0,0,6,0'/>
              <Setter Property='Template'>
                <Setter.Value>
                  <ControlTemplate TargetType='ScrollBar'>
                    <Border CornerRadius='2' Background='{track}'>
                      <Track x:Name='PART_Track' IsDirectionReversed='True'>
                        <Track.Thumb>
                          <Thumb>
                            <Thumb.Template>
                              <ControlTemplate TargetType='Thumb'>
                                <Border x:Name='B' CornerRadius='2' Background='{thumb}' Opacity='0.7'/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='1'/></Trigger>
                                  <Trigger Property='IsDragging' Value='True'><Setter TargetName='B' Property='Opacity' Value='1'/></Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            ";
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    /// <summary>Кнопка без системной рамки и подсветки.</summary>
    private static ControlTemplate FlatButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HAlign.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VAlign.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        return t;
    }
}
