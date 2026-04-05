using System.Drawing;

namespace ReQuit.Services;

/// <summary>Иконка из встроенного <c>app.ico</c> (тот же файл, что <see cref="ApplicationIcon"/> в csproj).</summary>
public static class AppIconFactory
{
    private const string PackUri = "pack://application:,,,/app.ico";

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
}
