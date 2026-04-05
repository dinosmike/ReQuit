using System.Globalization;
using System.Runtime.InteropServices;

namespace ReQuit.Services;

public static class AppLanguageCatalog
{
    private const uint MuiLanguageName = 0x8;

    public sealed record LanguageEntry(string Code, string NativeName);

    public static readonly IReadOnlyList<LanguageEntry> All = new[]
    {
        new LanguageEntry("ru", "Русский"),
        new LanguageEntry("en", "English")
    };

    public static bool IsSupported(string? code) =>
        !string.IsNullOrEmpty(code) && All.Any(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? code)
    {
        if (IsSupported(code))
            return code!;
        return ResolveDefaultLanguage();
    }

    public static string ResolveDefaultLanguage()
    {
        try
        {
            var fromWindows = TryMapPreferredWindowsUiLanguage();
            if (!string.IsNullOrEmpty(fromWindows))
                return fromWindows;
        }
        catch
        {
            // ignored
        }

        try
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (IsSupported(two))
                return two;
        }
        catch
        {
            // ignored
        }

        return "en";
    }

    private static string? TryMapPreferredWindowsUiLanguage()
    {
        if (!TryGetFirstPreferredUiLanguageName(out var localeName))
            return null;

        try
        {
            var ci = CultureInfo.GetCultureInfo(localeName);
            var two = ci.TwoLetterISOLanguageName;
            if (IsSupported(two))
                return two;
        }
        catch (CultureNotFoundException)
        {
            // ignored
        }

        return null;
    }

    private static bool TryGetFirstPreferredUiLanguageName(out string localeName)
    {
        localeName = string.Empty;
        uint num = 0;
        uint cb = 0;
        if (!GetUserPreferredUILanguages(MuiLanguageName, out num, IntPtr.Zero, ref cb) || cb == 0)
            return false;

        var ptr = Marshal.AllocHGlobal((int)(cb * sizeof(char)));
        try
        {
            if (!GetUserPreferredUILanguages(MuiLanguageName, out num, ptr, ref cb))
                return false;

            var s = Marshal.PtrToStringUni(ptr);
            if (string.IsNullOrEmpty(s))
                return false;

            var idx = s.IndexOf('\0');
            if (idx >= 0)
                s = s[..idx];
            s = s.Trim();
            if (string.IsNullOrEmpty(s))
                return false;

            localeName = s;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserPreferredUILanguages(
        uint dwFlags,
        out uint pulNumLanguages,
        IntPtr pwszLanguagesBuffer,
        ref uint pcchLanguagesBuffer);
}
