using System;
using System.Linq;
using System.Windows;

namespace NeuralTask.Theming;

public enum ThemeMode
{
    Dark,
    Light
}

public static class ThemeManager
{
    private const string DarkThemeSource = "pack://application:,,,/NeuralTask.Theming;component/Themes/DarkTheme.xaml";
    private const string LightThemeSource = "pack://application:,,,/NeuralTask.Theming;component/Themes/LightTheme.xaml";

    private static bool _initialized = false;

    public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

    public static event EventHandler<ThemeMode>? ThemeChanged;

    public static void ApplyTheme(ThemeMode theme)
    {
        if (_initialized && CurrentTheme == theme) return;

        var app = Application.Current;
        if (app == null) return;

        var targetUri = new Uri(theme == ThemeMode.Dark ? DarkThemeSource : LightThemeSource, UriKind.Absolute);

        // Find existing theme dictionary in root merged dictionaries
        var existingDict = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null &&
                (d.Source.OriginalString.Contains("DarkTheme.xaml") ||
                 d.Source.OriginalString.Contains("LightTheme.xaml") ||
                 d.Source.OriginalString.Contains("DesignTokens.xaml")));

        var newDict = new ResourceDictionary { Source = targetUri };

        if (existingDict != null)
        {
            var index = app.Resources.MergedDictionaries.IndexOf(existingDict);
            app.Resources.MergedDictionaries[index] = newDict;
        }
        else
        {
            app.Resources.MergedDictionaries.Add(newDict);
        }

        CurrentTheme = theme;
        _initialized = true;
        ThemeChanged?.Invoke(null, theme);
    }
}
