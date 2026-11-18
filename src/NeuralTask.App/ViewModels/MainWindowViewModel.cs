using System;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.App.Services;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ProcessMonitorCoordinator _coordinator;
    private readonly ISettingsService _settingsService;
    private readonly IProcessActionService _actionService;
    private readonly TrayIconService _trayIconService;
    private string _statusBadgeText = "SYS.NODE: ONLINE";
    private int _selectedTabIndex = 0;

    public DashboardViewModel Dashboard { get; }
    public ProcessListViewModel ProcessList { get; }
    public PerformanceViewModel Performance { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }

    public event Action? RequestRestoreWindow;
    public event Action? RequestExitApplication;

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
        ISettingsService settingsService,
        IProcessActionService actionService,
        TrayIconService trayIconService,
        IStartupScoreEngine startupScoreEngine,
        IProcessMonitorService monitorService,
        IHeuristicFlagEngine heuristicFlagEngine,
        IProcessInfoService processInfoService)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _actionService = actionService ?? throw new ArgumentNullException(nameof(actionService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));

        Dashboard = new DashboardViewModel();
        ProcessList = new ProcessListViewModel(_actionService, heuristicFlagEngine, processInfoService);
        Performance = new PerformanceViewModel();
        Startup = new StartupViewModel(startupScoreEngine, monitorService);
        Settings = new SettingsViewModel(_settingsService, _coordinator, heuristicFlagEngine);

        _coordinator.UpdateReceived += OnMonitoringUpdateReceived;

        _trayIconService.RestoreRequested += () => RequestRestoreWindow?.Invoke();
        _trayIconService.ExitRequested += () => RequestExitApplication?.Invoke();

        // Initial background load of startup matrix
        _ = Startup.LoadEntriesAsync();
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

        // 4. Update System Tray Icon live mini-graph & tooltip
        _trayIconService.UpdateMetrics(update.Processes, update.SystemMetrics);

        // 5. Update Header Status
        StatusBadgeText = $"SYS.NODE: ONLINE // {update.Processes.Count} TASKS";
    }

    public void Dispose()
    {
        _coordinator.UpdateReceived -= OnMonitoringUpdateReceived;
        _coordinator.Dispose();
        _trayIconService.Dispose();
    }
}
