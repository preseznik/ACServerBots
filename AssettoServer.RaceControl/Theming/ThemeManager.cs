using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using AssettoServer.RaceControl.Core.Storage;

namespace AssettoServer.RaceControl.Theming;

[SupportedOSPlatform("windows")]
public static class ThemeManager
{
    private const string ThemeMarker = "Themes/";
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    public static AppThemeMode AppliedTheme { get; private set; } = AppThemeMode.Dark;

    public static void Apply(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var application = Application.Current ?? throw new InvalidOperationException("WPF application is not initialized.");
        var resolved = Resolve(settings.Theme);
        var dictionaries = application.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(ThemeMarker, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Themes/{resolved}.xaml", UriKind.Relative),
        });
        application.Resources["GridRowHeight"] = settings.CompactGridRows ? 34d : 42d;
        AppliedTheme = resolved;
        foreach (Window window in application.Windows)
        {
            ApplyWindowChrome(window);
        }
    }

    public static void ApplyWindowChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = AppliedTheme == AppThemeMode.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
        }
    }

    public static AppThemeMode Resolve(AppThemeMode configured)
    {
        if (configured != AppThemeMode.System)
        {
            return configured;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0
            ? AppThemeMode.Light
            : AppThemeMode.Dark;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
