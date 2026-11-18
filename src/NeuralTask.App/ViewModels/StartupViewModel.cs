using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class StartupViewModel : ViewModelBase
{
    private readonly IStartupScoreEngine _engine;
    private readonly IProcessMonitorService _monitorService;
    private readonly List<StartupEntryItemViewModel> _allEntries = new();

    private string _searchText = string.Empty;
    private bool _isLoading;
    private int _totalStartupApps;
    private int _averageImpactScore;
    private int _highImpactCount;

    private bool _adminBannerVisible;
    private bool _isStatusVisible;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;

    public ObservableCollection<StartupEntryItemViewModel> FilteredEntries { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public int TotalStartupApps
    {
        get => _totalStartupApps;
        private set => SetProperty(ref _totalStartupApps, value);
    }

    public int AverageImpactScore
    {
        get => _averageImpactScore;
        private set => SetProperty(ref _averageImpactScore, value);
    }

    public int HighImpactCount
    {
        get => _highImpactCount;
        private set => SetProperty(ref _highImpactCount, value);
    }

    public bool IsAdministrator => _engine.IsAdministrator;

    public bool AdminBannerVisible
    {
        get => _adminBannerVisible;
        set => SetProperty(ref _adminBannerVisible, value);
    }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        set => SetProperty(ref _isStatusVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        set => SetProperty(ref _statusIsError, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand RelaunchAsAdminCommand { get; }
    public ICommand DismissBannerCommand { get; }
    public ICommand DismissStatusNotificationCommand { get; }

    public StartupViewModel(IStartupScoreEngine engine, IProcessMonitorService monitorService)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));

        _adminBannerVisible = !_engine.IsAdministrator;

        RefreshCommand = new RelayCommand(async () => await LoadEntriesAsync());
        RelaunchAsAdminCommand = new RelayCommand(RelaunchAsAdministrator);
        DismissBannerCommand = new RelayCommand(() => AdminBannerVisible = false);
        DismissStatusNotificationCommand = new RelayCommand(() => IsStatusVisible = false);
    }

    public async Task LoadEntriesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            var liveSnapshots = _monitorService.CurrentSnapshot;
            var rawEntries = await _engine.EnumerateStartupEntriesAsync(liveSnapshots);

            _allEntries.Clear();
            foreach (var entry in rawEntries)
            {
                _allEntries.Add(new StartupEntryItemViewModel(entry, _engine, ShowNotification));
            }

            TotalStartupApps = _allEntries.Count;
            AverageImpactScore = _allEntries.Count > 0
                ? (int)Math.Round(_allEntries.Average(e => e.Score))
                : 0;
            HighImpactCount = _allEntries.Count(e => e.Score >= 60);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            ShowNotification($"Failed to load startup matrix: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        var items = string.IsNullOrWhiteSpace(query)
            ? _allEntries
            : _allEntries.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Location.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.ImpactRating.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredEntries.Add(item);
        }
    }

    private void ShowNotification(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        IsStatusVisible = true;
    }

    private void RelaunchAsAdministrator()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ShowNotification($"Failed to elevate: {ex.Message}", true);
        }
    }
}
