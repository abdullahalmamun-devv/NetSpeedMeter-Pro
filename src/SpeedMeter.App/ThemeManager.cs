using System;
using System.Windows;
using Microsoft.Win32;

namespace SpeedMeter.App;

public static class ThemeManager
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static string CurrentThemeName { get; private set; } = "Dark";

    public static void ApplyTheme(string? theme)
    {
        var effectiveTheme = string.IsNullOrWhiteSpace(theme) ? "Dark" : theme;
        CurrentThemeName = effectiveTheme;

        var isLight = effectiveTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        if (effectiveTheme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            isLight = IsSystemInLightTheme();
        }

        var themeFileName = isLight ? "ThemeLight.xaml" : "ThemeDark.xaml";
        var app = System.Windows.Application.Current;
        if (app is null) return;

        try
        {
            var packUri = new Uri($"pack://application:,,,/SpeedMeter.App;component/Themes/{themeFileName}", UriKind.Absolute);
            var newDict = new ResourceDictionary { Source = packUri };

            var merged = app.Resources.MergedDictionaries;
            if (merged.Count >= 2)
            {
                merged[1] = newDict;
            }
            else
            {
                merged.Add(newDict);
            }
        }
        catch
        {
            try
            {
                var relUri = new Uri($"Themes/{themeFileName}", UriKind.Relative);
                var newDict = new ResourceDictionary { Source = relUri };
                var merged = app.Resources.MergedDictionaries;
                if (merged.Count >= 2)
                {
                    merged[1] = newDict;
                }
                else
                {
                    merged.Add(newDict);
                }
            }
            catch
            {
                // Fallback for test / headless environments where pack resources are not loaded
            }
        }
    }

    public static bool IsSystemInLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue(AppsUseLightThemeValue) is int val)
            {
                return val == 1;
            }
        }
        catch
        {
            // Standard fallback when registry access is restricted
        }
        return false;
    }
}
