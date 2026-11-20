using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.App.Services;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable, IAsyncDisposable
{
    private readonly ProcessMonitorCoordinator _coordinator;
    private readonly ISettingsService _settingsService;
    private readonly IProcessActionService _actionService;
    private readonly TrayIconService _trayIconService;
    private readonly IPluginManager _pluginManager;
    private readonly Action _onRestoreRequested;
    private readonly Action _onExitRequested;
    private string _statusBadgeText = "System online";
    private int _selectedTabIndex = 0;
    private readonly SemaphoreSlim _pluginTickLock = new(1, 1);
    private Task? _lastPluginTickTask;
    private bool _isDisposed;

    public Task StartupLoadTask { get; }

    public DashboardViewModel Dashboard { get; }
    public ProcessListViewModel ProcessList { get; }
    public PerformanceViewModel Performance { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }
    public IPluginManager PluginManager => _pluginManager;

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
        IProcessInfoService processInfoService,
        IHistoryStore historyStore,
        IExportService exportService,
        IPluginManager pluginManager)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _actionService = actionService ?? throw new ArgumentNullException(nameof(actionService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));

        Dashboard = new DashboardViewModel(historyStore, exportService, heuristicFlagEngine, startupEngine: startupScoreEngine);
        ProcessList = new ProcessListViewModel(_actionService, heuristicFlagEngine, processInfoService);
        Performance = new PerformanceViewModel();
        Startup = new StartupViewModel(startupScoreEngine, monitorService);
        Settings = new SettingsViewModel(_settingsService, _coordinator, heuristicFlagEngine, historyStore, _pluginManager);

        _coordinator.UpdateReceived += OnMonitoringUpdateReceived;

        _onRestoreRequested = () => RequestRestoreWindow?.Invoke();
        _onExitRequested = () => RequestExitApplication?.Invoke();
        _trayIconService.RestoreRequested += _onRestoreRequested;
        _trayIconService.ExitRequested += _onExitRequested;

        // Discover and load local extension plugins from /plugins
        try
        {
            _pluginManager.DiscoverAndLoadPlugins();
        }
        catch
        {
        }

        // Initial tracked background load of startup matrix
        StartupLoadTask = Task.Run(async () =>
        {
            try
            {
                await Startup.LoadEntriesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Startup matrix initial load error: {ex}");
            }
        });
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _coordinator.StopAsync(cancellationToken).ConfigureAwait(false);
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

        // 5. Dispatch telemetry tick to loaded plugins on background thread safely with ownership tracking
        var pluginContext = new NeuralTask.Contracts.PluginTelemetryContext(
            update.SystemMetrics.CpuPercent,
            update.SystemMetrics.TotalMemoryBytes,
            update.SystemMetrics.UsedMemoryBytes,
            update.SystemMetrics.AvailableMemoryBytes,
            update.Processes.Count,
            DateTime.UtcNow);

        if (!_isDisposed && _pluginTickLock.Wait(0))
        {
            _lastPluginTickTask = Task.Run(() =>
            {
                try
                {
                    if (!_isDisposed)
                    {
                        _pluginManager.DispatchTelemetryTick(pluginContext);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Plugin telemetry tick dispatch failed: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        _pluginTickLock.Release();
                    }
                    catch (ObjectDisposedException) { }
                }
            });
        }

        // 6. Update Header Status
        StatusBadgeText = $"Online • {update.Processes.Count} processes";
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _coordinator.UpdateReceived -= OnMonitoringUpdateReceived;
        _trayIconService.RestoreRequested -= _onRestoreRequested;
        _trayIconService.ExitRequested -= _onExitRequested;

        if (StartupLoadTask != null && !StartupLoadTask.IsCompleted)
        {
            try
            {
                await StartupLoadTask.ConfigureAwait(false);
            }
            catch { }
        }

        var lastTick = _lastPluginTickTask;
        if (lastTick != null && !lastTick.IsCompleted)
        {
            try
            {
                await lastTick.ConfigureAwait(false);
            }
            catch { }
        }
        _pluginTickLock.Dispose();

        await _coordinator.DisposeAsync().ConfigureAwait(false);
        Dashboard.Dispose();
        await ProcessList.DisposeAsync().ConfigureAwait(false);
        Startup.Dispose();
        Settings.Dispose();
        _trayIconService.Dispose();
        _pluginManager.Dispose();
        RequestRestoreWindow = null;
        RequestExitApplication = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _coordinator.UpdateReceived -= OnMonitoringUpdateReceived;
        _trayIconService.RestoreRequested -= _onRestoreRequested;
        _trayIconService.ExitRequested -= _onExitRequested;

        if (StartupLoadTask != null && !StartupLoadTask.IsCompleted)
        {
            try { StartupLoadTask.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        }

        var lastTick = _lastPluginTickTask;
        if (lastTick != null && !lastTick.IsCompleted)
        {
            try { lastTick.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        }
        _pluginTickLock.Dispose();

        _coordinator.Dispose();
        Dashboard.Dispose();
        ProcessList.Dispose();
        Startup.Dispose();
        Settings.Dispose();
        _trayIconService.Dispose();
        _pluginManager.Dispose();
        RequestRestoreWindow = null;
        RequestExitApplication = null;
    }
}
