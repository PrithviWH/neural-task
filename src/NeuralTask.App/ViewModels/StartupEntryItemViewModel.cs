using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class StartupEntryItemViewModel : ViewModelBase
{
    private readonly StartupEntry _entry;
    private readonly IStartupScoreEngine _engine;
    private readonly Action<string, bool> _onStatusNotification;
    private bool _isExpanded;

    public StartupEntry Entry => _entry;

    public string Id => _entry.Id;
    public string Name => _entry.Name;
    public string Command => _entry.Command;
    public string? ExecutablePath => _entry.ExecutablePath;
    public string Location => _entry.Location;
    public StartupSourceType SourceType => _entry.SourceType;

    public string SourceTypeDisplay => _entry.SourceType switch
    {
        StartupSourceType.RegistryRunCurrentUser => "HKCU // USER RUN",
        StartupSourceType.RegistryRunLocalMachine => "HKLM // SYSTEM RUN",
        StartupSourceType.StartupFolderUser => "STARTUP // USER FOLDER",
        StartupSourceType.StartupFolderCommon => "STARTUP // ALL USERS",
        _ => "SCHEDULED TASK"
    };

    public bool IsEnabled
    {
        get => _entry.IsEnabled;
        set
        {
            if (_entry.IsEnabled != value)
            {
                bool success = _engine.SetEntryEnabled(_entry, value);
                if (success)
                {
                    OnPropertyChanged();
                    _onStatusNotification($"'{Name}' is now {(value ? "ENABLED" : "DISABLED")}.", false);
                }
                else
                {
                    // Failed (e.g. requires administrator privileges for HKLM)
                    OnPropertyChanged();
                    _onStatusNotification($"Failed to update '{Name}'. Administrator elevation is required for machine-wide entries.", true);
                }
            }
        }
    }

    public int? AssociatedPid => _entry.AssociatedPid;
    public bool IsRunning => _entry.AssociatedPid.HasValue;

    public int Score => _entry.Score?.TotalScore ?? 0;
    public double TimeScore => _entry.Score?.TimeScore ?? 0.0;
    public double ResourceScore => _entry.Score?.ResourceScore ?? 0.0;
    public double TimeContributionPercent => _entry.Score?.TimeContributionPercent ?? 50.0;
    public double ResourceContributionPercent => _entry.Score?.ResourceContributionPercent ?? 50.0;

    public double TimeBarWidth => Math.Max(2.0, Math.Round(180.0 * (TimeContributionPercent / 100.0), 1));
    public double ResourceBarWidth => Math.Max(2.0, Math.Round(180.0 * (ResourceContributionPercent / 100.0), 1));

    public double ActiveDurationSeconds => _entry.Score?.ActiveDurationSeconds ?? 0.0;
    public double BootDelaySeconds => _entry.Score?.BootDelaySeconds ?? 0.0;
    public double CpuTimeSeconds => _entry.Score?.CpuTimeSeconds ?? 0.0;
    public long IoBytes => _entry.Score?.IoBytes ?? 0;

    public string IoDisplay
    {
        get
        {
            double mb = (_entry.Score?.IoBytes ?? 0) / (1024.0 * 1024.0);
            return mb < 1.0 ? $"{mb:0.00} MB" : $"{mb:0.0} MB";
        }
    }

    public string ImpactRating => _entry.Score?.ImpactRating ?? "Minimal";
    public string Summary => _entry.Score?.Summary ?? "No background boot overhead detected.";

    public Brush ImpactBrush
    {
        get
        {
            return Score switch
            {
                >= 80 => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)), // Severe: Red
                >= 60 => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)), // High: Orange
                >= 40 => new SolidColorBrush(Color.FromRgb(0x79, 0x28, 0xCA)), // Moderate: Violet
                >= 20 => new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF)), // Low: Neon Cyan
                _ => new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99))      // Minimal: Mint Green
            };
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ICommand ToggleExpandCommand { get; }

    public StartupEntryItemViewModel(
        StartupEntry entry,
        IStartupScoreEngine engine,
        Action<string, bool> onStatusNotification)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _onStatusNotification = onStatusNotification ?? throw new ArgumentNullException(nameof(onStatusNotification));

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }
}
