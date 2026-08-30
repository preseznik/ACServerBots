using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ACEditor.Core.Tools;
using Microsoft.Win32;

namespace ACEditor.App.Themes;

public static class ThemeManager
{
    private const string DarkDictionary = "Colors.Dark.xaml";
    private const string LightDictionary = "Colors.Light.xaml";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    public static AppTheme CurrentSetting { get; private set; } = AppTheme.System;
    public static AppTheme EffectiveTheme { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        CurrentSetting = theme;
        EffectiveTheme = theme == AppTheme.System ? ReadSystemTheme() : theme;

        ResourceDictionary resources = Application.Current.Resources;
        ResourceDictionary replacement = new()
        {
            Source = new Uri($"Themes/Colors.{EffectiveTheme}.xaml", UriKind.Relative)
        };

        int themeIndex = resources.MergedDictionaries
            .Select((dictionary, index) => (dictionary, index))
            .Where(item => IsThemeDictionary(item.dictionary))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();

        if (themeIndex >= 0) resources.MergedDictionaries[themeIndex] = replacement;
        else resources.MergedDictionaries.Insert(0, replacement);

        foreach (Window window in Application.Current.Windows) ApplyWindow(window);
    }

    public static void ApplyWindow(Window window)
    {
        void UpdateTitleBar()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int enabled = EffectiveTheme == AppTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle == IntPtr.Zero)
            window.SourceInitialized += (_, _) => UpdateTitleBar();
        else
            UpdateTitleBar();
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        string source = dictionary.Source?.OriginalString ?? string.Empty;
        return source.EndsWith(DarkDictionary, StringComparison.OrdinalIgnoreCase) ||
               source.EndsWith(LightDictionary, StringComparison.OrdinalIgnoreCase);
    }

    private static AppTheme ReadSystemTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0
                ? AppTheme.Light
                : AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
