using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ReQuit.Services;

/// <summary>Иконка из встроенного <c>app.ico</c> (тот же файл, что <see cref="ApplicationIcon"/> в csproj).</summary>
public static class AppIconFactory
{
    private const string PackUri = "pack://application:,,,/app.ico";

    /// <summary>Размер растровой иконки; больше 16 — лучше масштабируется в области уведомлений.</summary>
    private const int TrayGlyphSizePx = 64;

    /// <summary>Цвет цифр при отсчёте до выключения (как акцент «выключение» в UI).</summary>
    private static readonly System.Drawing.Color TrayGlyphShutdown = System.Drawing.Color.Red;

    /// <summary>Цвет цифр при отсчёте до сигнала (#01a16a).</summary>
    private static readonly System.Drawing.Color TrayGlyphSignal = System.Drawing.Color.FromArgb(0x01, 0xa1, 0x6a);

    public static Icon CreateTrayIcon()
    {
        var streamInfo = System.Windows.Application.GetResourceStream(new Uri(PackUri, UriKind.Absolute));
        if (streamInfo?.Stream == null)
            throw new InvalidOperationException("Embedded app.ico not found.");

        using (streamInfo.Stream)
        {
            using var icon = new Icon(streamInfo.Stream);
            return (Icon)icon.Clone();
        }
    }

    /// <summary>Текст на чёрном фоне (часы «1ч»/«1h», минуты, секунды «00»…). Цвет — выключение (красный) или сигнал (#01a16a).</summary>
    public static Icon CreateTrayCountdownGlyph(string text, bool actionIsSignal)
    {
        var fore = actionIsSignal ? TrayGlyphSignal : TrayGlyphShutdown;

        using var bmp = new Bitmap(TrayGlyphSizePx, TrayGlyphSizePx, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float em = FindLargestFittingFontEm(g, text, TrayGlyphSizePx);
            using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, TrayGlyphSizePx, TrayGlyphSizePx), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    /// <summary>Подбирает максимальный размер шрифта (px), при котором строка помещается в квадрат.</summary>
    private static float FindLargestFittingFontEm(Graphics g, string text, int boxPx)
    {
        const string FontName = "Segoe UI";
        const float MinEm = 5f;
        // Верхняя граница — почти вся высота квадрата (две цифры шире, чем выше).
        float hi = boxPx * 0.98f;
        float best = MinEm;

        for (float em = hi; em >= MinEm; em -= 0.35f)
        {
            using var font = new Font(FontName, em, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (sz.Width <= boxPx && sz.Height <= boxPx)
            {
                best = em;
                break;
            }
        }

        return best;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
