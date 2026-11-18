using System;
using NeuralTask.App.Services;
using NeuralTask.Theming;

namespace NeuralTask.App.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ProcessMonitorCoordinator _coordinator;

    private ThemeMode _selectedTheme;
    private int _selectedIntervalMs;
    private bool _isAmbientAnimationEnabled;
    private bool _isMinimizeToTrayEnabled;
    private string _trayIconMetric = "CPU";

    public bool IsMinimizeToTrayEnabled
    {
        get => _isMinimizeToTrayEnabled;
        set
        {
            if (SetProperty(ref _isMinimizeToTrayEnabled, value))
            {
                Persist();
            }
        }
    }

    public string TrayIconMetric
    {
        get => _trayIconMetric;
        set
        {
            if (SetProperty(ref _trayIconMetric, value))
            {
                Persist();
                OnPropertyChanged(nameof(IsTrayMetricCpu));
                OnPropertyChanged(nameof(IsTrayMetricRam));
            }
        }
    }

    public bool IsTrayMetricCpu
    {
        get => _trayIconMetric.Equals("CPU", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) TrayIconMetric = "CPU";
        }
    }

    public bool IsTrayMetricRam
    {
        get => _trayIconMetric.Equals("RAM", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) TrayIconMetric = "RAM";
        }
    }

    public ThemeMode SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                ThemeManager.ApplyTheme(value);
                Persist();
                OnPropertyChanged(nameof(IsDarkThemeSelected));
                OnPropertyChanged(nameof(IsLightThemeSelected));
            }
        }
    }

    public bool IsDarkThemeSelected
    {
        get => _selectedTheme == ThemeMode.Dark;
        set
        {
            if (value && _selectedTheme != ThemeMode.Dark)
            {
                SelectedTheme = ThemeMode.Dark;
            }
        }
    }

    public bool IsLightThemeSelected
    {
        get => _selectedTheme == ThemeMode.Light;
        set
        {
            if (value && _selectedTheme != ThemeMode.Light)
            {
                SelectedTheme = ThemeMode.Light;
            }
        }
    }

    public int SelectedIntervalMs
    {
        get => _selectedIntervalMs;
        set
        {
            if (SetProperty(ref _selectedIntervalMs, value))
            {
                _coordinator.SetInterval(TimeSpan.FromMilliseconds(value));
                Persist();
                OnPropertyChanged(nameof(IsInterval500));
                OnPropertyChanged(nameof(IsInterval1000));
                OnPropertyChanged(nameof(IsInterval2000));
            }
        }
    }

    public bool IsInterval500
    {
        get => _selectedIntervalMs == 500;
        set
        {
            if (value) SelectedIntervalMs = 500;
        }
    }

    public bool IsInterval1000
    {
        get => _selectedIntervalMs == 1000;
        set
        {
            if (value) SelectedIntervalMs = 1000;
        }
    }

    public bool IsInterval2000
    {
        get => _selectedIntervalMs == 2000;
        set
        {
            if (value) SelectedIntervalMs = 2000;
        }
    }

    public bool IsAmbientAnimationEnabled
    {
        get => _isAmbientAnimationEnabled;
        set
        {
            if (SetProperty(ref _isAmbientAnimationEnabled, value))
            {
                Persist();
            }
        }
    }

    public SettingsViewModel(ISettingsService settingsService, ProcessMonitorCoordinator coordinator)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        var initial = _settingsService.CurrentSettings;
        _selectedTheme = initial.Theme;
        _selectedIntervalMs = initial.RefreshIntervalMs;
        _isAmbientAnimationEnabled = initial.IsAmbientAnimationEnabled;
        _isMinimizeToTrayEnabled = initial.IsMinimizeToTrayEnabled;
        _trayIconMetric = initial.TrayIconMetric;

        // Apply loaded initial settings
        ThemeManager.ApplyTheme(_selectedTheme);
        _coordinator.SetInterval(TimeSpan.FromMilliseconds(_selectedIntervalMs));
    }

    private void Persist()
    {
        _settingsService.SaveSettings(new AppSettings(
            Theme: _selectedTheme,
            RefreshIntervalMs: _selectedIntervalMs,
            IsAmbientAnimationEnabled: _isAmbientAnimationEnabled,
            IsMinimizeToTrayEnabled: _isMinimizeToTrayEnabled,
            TrayIconMetric: _trayIconMetric
        ));
    }
}
