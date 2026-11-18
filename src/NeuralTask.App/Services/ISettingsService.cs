using System;
using NeuralTask.Theming;

namespace NeuralTask.App.Services;

public record AppSettings(
    ThemeMode Theme = ThemeMode.Dark,
    int RefreshIntervalMs = 1000,
    bool IsAmbientAnimationEnabled = true,
    bool IsMinimizeToTrayEnabled = true,
    string TrayIconMetric = "CPU"
);

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    void SaveSettings(AppSettings settings);
    event EventHandler<AppSettings>? SettingsChanged;
}
