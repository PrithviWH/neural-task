using System;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.App.Services;

namespace NeuralTask.App.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ProcessMonitorCoordinator _coordinator;
    private readonly ISettingsService _settingsService;
    private string _statusBadgeText = "SYS.NODE: ONLINE";
    private int _selectedTabIndex = 0;

    public DashboardViewModel Dashboard { get; }
    public ProcessListViewModel ProcessList { get; }
    public PerformanceViewModel Performance { get; }
    public SettingsViewModel Settings { get; }

    public string StatusBadgeText
    {
        get => _statusBadgeText;
        private set => SetProperty(ref _statusBadgeText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public MainWindowViewModel(
        ProcessMonitorCoordinator coordinator,
        ISettingsService settingsService)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        Dashboard = new DashboardViewModel();
        ProcessList = new ProcessListViewModel();
        Performance = new PerformanceViewModel();
        Settings = new SettingsViewModel(_settingsService, _coordinator);

        _coordinator.UpdateReceived += OnMonitoringUpdateReceived;
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopMonitoringAsync()
    {
        await _coordinator.StopAsync().ConfigureAwait(false);
    }

    private void OnMonitoringUpdateReceived(ProcessMonitoringUpdate update)
    {
        // 1. Update Dashboard
        Dashboard.ApplyMetrics(update.Processes, update.SystemMetrics);

        // 2. Update Process List
        ProcessList.ApplySnapshot(update.Processes);

        // 3. Update Performance Charts
        Performance.ApplyMetrics(update.SystemMetrics);

        // 4. Update Header Status
        StatusBadgeText = $"SYS.NODE: ONLINE // {update.Processes.Count} TASKS";
    }

    public void Dispose()
    {
        _coordinator.UpdateReceived -= OnMonitoringUpdateReceived;
        _coordinator.Dispose();
    }
}
