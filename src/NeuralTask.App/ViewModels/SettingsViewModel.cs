using System;
using NeuralTask.App.Services;
using NeuralTask.Theming;

namespace NeuralTask.App.ViewModels;

public class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ProcessMonitorCoordinator _coordinator;
    private readonly NeuralTask.Core.Services.IHeuristicFlagEngine? _heuristicEngine;
    private readonly NeuralTask.Core.Services.IHistoryStore? _historyStore;
    private readonly IPluginManager? _pluginManager;

    public IReadOnlyList<SafePluginPanel> LoadedPlugins => _pluginManager?.LoadedPanels ?? Array.Empty<SafePluginPanel>();
    public string PluginsDirectory => _pluginManager?.PluginsDirectory ?? "";

    public void RefreshPluginsList()
    {
        OnPropertyChanged(nameof(LoadedPlugins));
    }

    private readonly Action<SafePluginPanel> _onPluginPanelLoaded;
    private readonly Action<SafePluginPanel> _onPluginPanelFaulted;

    private ThemeMode _selectedTheme;
    private int _selectedIntervalMs;
    private bool _isAmbientAnimationEnabled;
    private bool _isMinimizeToTrayEnabled;
    private string _trayIconMetric = "CPU";
    private NeuralTask.Core.Services.HeuristicSensitivity _heuristicSensitivity = NeuralTask.Core.Services.HeuristicSensitivity.Conservative;
    private int _historyRetentionMinutes = 30;
    private bool _isHistoryPersistenceEnabled = false;

    public int HistoryRetentionMinutes
    {
        get => _historyRetentionMinutes;
        set
        {
            if (SetProperty(ref _historyRetentionMinutes, value))
            {
                if (_historyStore != null)
                {
                    _historyStore.RetentionWindow = TimeSpan.FromMinutes(value);
                }
                Persist();
                OnPropertyChanged(nameof(IsRetention15));
                OnPropertyChanged(nameof(IsRetention30));
                OnPropertyChanged(nameof(IsRetention60));
            }
        }
    }

    public bool IsRetention15
    {
        get => _historyRetentionMinutes == 15;
        set
        {
            if (value) HistoryRetentionMinutes = 15;
        }
    }

    public bool IsRetention30
    {
        get => _historyRetentionMinutes == 30;
        set
        {
            if (value) HistoryRetentionMinutes = 30;
        }
    }

    public bool IsRetention60
    {
        get => _historyRetentionMinutes == 60;
        set
        {
            if (value) HistoryRetentionMinutes = 60;
        }
    }

    public bool IsHistoryPersistenceEnabled
    {
        get => _isHistoryPersistenceEnabled;
        set
        {
            if (SetProperty(ref _isHistoryPersistenceEnabled, value))
            {
                if (_historyStore != null)
                {
                    _historyStore.IsPersistenceEnabled = value;
                }
                Persist();
            }
        }
    }

    public NeuralTask.Core.Services.HeuristicSensitivity HeuristicSensitivity
    {
        get => _heuristicSensitivity;
        set
        {
            if (SetProperty(ref _heuristicSensitivity, value))
            {
                if (_heuristicEngine != null)
                {
                    _heuristicEngine.Sensitivity = value;
                }
                Persist();
                OnPropertyChanged(nameof(IsSensitivityConservative));
                OnPropertyChanged(nameof(IsSensitivityStandard));
                OnPropertyChanged(nameof(IsSensitivityAggressive));
            }
        }
    }

    public bool IsSensitivityConservative
    {
        get => _heuristicSensitivity == NeuralTask.Core.Services.HeuristicSensitivity.Conservative;
        set
        {
            if (value) HeuristicSensitivity = NeuralTask.Core.Services.HeuristicSensitivity.Conservative;
        }
    }

    public bool IsSensitivityStandard
    {
        get => _heuristicSensitivity == NeuralTask.Core.Services.HeuristicSensitivity.Standard;
        set
        {
            if (value) HeuristicSensitivity = NeuralTask.Core.Services.HeuristicSensitivity.Standard;
        }
    }

    public bool IsSensitivityAggressive
    {
        get => _heuristicSensitivity == NeuralTask.Core.Services.HeuristicSensitivity.Aggressive;
        set
        {
            if (value) HeuristicSensitivity = NeuralTask.Core.Services.HeuristicSensitivity.Aggressive;
        }
    }

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

    public SettingsViewModel(
        ISettingsService settingsService,
        ProcessMonitorCoordinator coordinator,
        NeuralTask.Core.Services.IHeuristicFlagEngine? heuristicEngine = null,
        NeuralTask.Core.Services.IHistoryStore? historyStore = null,
        IPluginManager? pluginManager = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _heuristicEngine = heuristicEngine;
        _historyStore = historyStore;
        _pluginManager = pluginManager;

        _onPluginPanelLoaded = _ => RefreshPluginsList();
        _onPluginPanelFaulted = _ => RefreshPluginsList();

        if (_pluginManager != null)
        {
            _pluginManager.PanelLoaded += _onPluginPanelLoaded;
            _pluginManager.PanelFaulted += _onPluginPanelFaulted;
        }

        var initial = _settingsService.CurrentSettings;
        _selectedTheme = initial.Theme;
        _selectedIntervalMs = initial.RefreshIntervalMs;
        _isAmbientAnimationEnabled = initial.IsAmbientAnimationEnabled;
        _isMinimizeToTrayEnabled = initial.IsMinimizeToTrayEnabled;
        _trayIconMetric = initial.TrayIconMetric;
        _heuristicSensitivity = initial.HeuristicSensitivity;
        _historyRetentionMinutes = initial.HistoryRetentionMinutes;
        _isHistoryPersistenceEnabled = initial.IsHistoryPersistenceEnabled;

        if (_heuristicEngine != null)
        {
            _heuristicEngine.Sensitivity = _heuristicSensitivity;
        }

        if (_historyStore != null)
        {
            _historyStore.RetentionWindow = TimeSpan.FromMinutes(_historyRetentionMinutes);
            _historyStore.IsPersistenceEnabled = _isHistoryPersistenceEnabled;
        }

        // Apply loaded initial settings
        ThemeManager.ApplyTheme(_selectedTheme);
        _coordinator.SetInterval(TimeSpan.FromMilliseconds(_selectedIntervalMs));
    }

    public void Persist()
    {
        _settingsService.SaveSettings(new AppSettings(
            Theme: _selectedTheme,
            RefreshIntervalMs: _selectedIntervalMs,
            IsAmbientAnimationEnabled: _isAmbientAnimationEnabled,
            IsMinimizeToTrayEnabled: _isMinimizeToTrayEnabled,
            TrayIconMetric: _trayIconMetric,
            HeuristicSensitivity: _heuristicSensitivity,
            HistoryRetentionMinutes: _historyRetentionMinutes,
            IsHistoryPersistenceEnabled: _isHistoryPersistenceEnabled
        ));
    }

    private bool _isDisposed;
    public int DisposeCount { get; private set; }

    public void Dispose()
    {
        DisposeCount++;
        if (_isDisposed) return;
        _isDisposed = true;

        if (_pluginManager != null)
        {
            _pluginManager.PanelLoaded -= _onPluginPanelLoaded;
            _pluginManager.PanelFaulted -= _onPluginPanelFaulted;
        }
    }
}
