using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

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

    private static readonly object GdiFontInitLock = new();
    private static PrivateFontCollection? _privateUiFonts;
    private static readonly List<GCHandle> _pinnedFontBytes = new();
    private static System.Drawing.FontFamily? _segoeUiGdiFamily;

    /// <summary>Segoe UI из <c>Fonts/*.ttf</c> для GDI+ (иконка трея), без системной установки.</summary>
    private static System.Drawing.FontFamily SegoeUiGdiFamily
    {
        get
        {
            EnsureGdiSegoeUiFonts();
            return _segoeUiGdiFamily ?? System.Drawing.FontFamily.GenericSansSerif;
        }
    }

    private static void EnsureGdiSegoeUiFonts()
    {
        if (_privateUiFonts != null)
            return;
        lock (GdiFontInitLock)
        {
            if (_privateUiFonts != null)
                return;

            var pfc = new PrivateFontCollection();
            foreach (var file in new[]
                     {
                         "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuil.ttf", "segoeuisl.ttf", "segoeuiz.ttf"
                     })
            {
                var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Fonts/{file}", UriKind.Absolute));
                if (info?.Stream == null)
                    continue;
                using (info.Stream)
                using (var ms = new MemoryStream())
                {
                    info.Stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    _pinnedFontBytes.Add(h);
                    pfc.AddMemoryFont(h.AddrOfPinnedObject(), bytes.Length);
                }
            }

            _privateUiFonts = pfc;
            foreach (var ff in pfc.Families)
            {
                if (ff.Name == "Segoe UI")
                {
                    _segoeUiGdiFamily = ff;
                    return;
                }
            }

            if (pfc.Families.Length > 0)
                _segoeUiGdiFamily = pfc.Families[0];
        }
    }

    public static Icon CreateTrayIcon()
    {
        var streamInfo = Application.GetResourceStream(new Uri(PackUri, UriKind.Absolute));
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
            using var font = new Font(SegoeUiGdiFamily, em, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
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
        const float MinEm = 5f;
        float hi = boxPx * 0.98f;
        float best = MinEm;

        for (float em = hi; em >= MinEm; em -= 0.35f)
        {
            using var font = new Font(SegoeUiGdiFamily, em, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = TextRenderer.MeasureText(g, text, font, new System.Drawing.Size(int.MaxValue, int.MaxValue),
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
