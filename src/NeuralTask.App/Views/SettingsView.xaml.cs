using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeuralTask.Theming;

namespace NeuralTask.App.Views;

public partial class SettingsView : UserControl
{
    public static event Action<bool>? AmbientAnimationToggled;
    private bool _isSyncingTheme = false;

    public SettingsView()
    {
        InitializeComponent();
        SyncWithCurrentTheme();
        ThemeManager.ThemeChanged += (_, _) => SyncWithCurrentTheme();
    }

    private void SyncWithCurrentTheme()
    {
        if (_isSyncingTheme) return;
        _isSyncingTheme = true;

        try
        {
            var isDark = ThemeManager.CurrentTheme == ThemeMode.Dark;
            if (RadioThemeDark != null && RadioThemeDark.IsChecked != isDark)
            {
                RadioThemeDark.IsChecked = isDark;
            }
            if (RadioThemeLight != null && RadioThemeLight.IsChecked == isDark)
            {
                RadioThemeLight.IsChecked = !isDark;
            }

            if (BorderThemeDark != null && BorderThemeLight != null)
            {
                BorderThemeDark.BorderThickness = isDark ? new Thickness(2) : new Thickness(1);
                BorderThemeLight.BorderThickness = !isDark ? new Thickness(2) : new Thickness(1);
            }
        }
        finally
        {
            _isSyncingTheme = false;
        }
    }

    private void OnSelectDarkTheme(object sender, MouseButtonEventArgs e)
    {
        if (ThemeManager.CurrentTheme != ThemeMode.Dark)
        {
            ThemeManager.ApplyTheme(ThemeMode.Dark);
        }
        SyncWithCurrentTheme();
    }

    private void OnSelectLightTheme(object sender, MouseButtonEventArgs e)
    {
        if (ThemeManager.CurrentTheme != ThemeMode.Light)
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
        }
        SyncWithCurrentTheme();
    }

    private void OnRadioThemeDarkChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingTheme) return;
        if (ThemeManager.CurrentTheme != ThemeMode.Dark)
        {
            ThemeManager.ApplyTheme(ThemeMode.Dark);
        }
    }

    private void OnRadioThemeLightChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingTheme) return;
        if (ThemeManager.CurrentTheme != ThemeMode.Light)
        {
            ThemeManager.ApplyTheme(ThemeMode.Light);
        }
    }

    private void OnAmbientAnimationToggled(object sender, RoutedEventArgs e)
    {
        if (CheckAmbientAnimation != null)
        {
            var isEnabled = CheckAmbientAnimation.IsChecked == true;
            CheckAmbientAnimation.Content = isEnabled ? "ACTIVE" : "DISABLED";
            AmbientAnimationToggled?.Invoke(isEnabled);
        }
    }
}
