using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Services;

namespace NeuralTask.App.Services;

public interface IAppShutdownCoordinator
{
    int ShutdownState { get; }
    Task<bool> ExecuteShutdownAsync(TimeSpan? stepTimeout = null, CancellationToken cancellationToken = default);
}

public class AppShutdownCoordinator : IAppShutdownCoordinator
{
    private readonly ISettingsService _settingsService;
    private readonly IHistoryStore? _historyStore;
    private readonly Func<CancellationToken, Task>? _stopMonitoringAction;
    private readonly Func<ValueTask>? _disposeAction;
    private readonly Func<Task>? _historyLoadTaskProvider;
    private readonly Action? _closeAction;
    private int _shutdownState = 0; // 0 = Active, 1 = Shutting down, 2 = Shutdown complete

    public int ShutdownState => Volatile.Read(ref _shutdownState);

    public AppShutdownCoordinator(
        ISettingsService settingsService,
        IHistoryStore? historyStore,
        MainWindowViewModel viewModel,
        Func<Task>? historyLoadTaskProvider = null,
        Action? closeAction = null)
        : this(
            settingsService,
            historyStore,
            ct => viewModel.StopMonitoringAsync(ct),
            () => viewModel.DisposeAsync(),
            historyLoadTaskProvider ?? (() => viewModel.StartupLoadTask),
            closeAction)
    {
    }

    public AppShutdownCoordinator(
        ISettingsService settingsService,
        IHistoryStore? historyStore,
        Func<CancellationToken, Task>? stopMonitoringAction,
        Func<ValueTask>? disposeAction,
        Func<Task>? historyLoadTaskProvider = null,
        Action? closeAction = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _historyStore = historyStore;
        _stopMonitoringAction = stopMonitoringAction;
        _disposeAction = disposeAction;
        _historyLoadTaskProvider = historyLoadTaskProvider;
        _closeAction = closeAction;
    }

    public async Task<bool> ExecuteShutdownAsync(TimeSpan? stepTimeout = null, CancellationToken cancellationToken = default)
    {
        // 1. Reject duplicate close requests or concurrent shutdown work
        if (Interlocked.CompareExchange(ref _shutdownState, 1, 0) != 0)
        {
            return false;
        }

        var timeout = stepTimeout ?? TimeSpan.FromSeconds(3);
        bool monitoringStoppedSuccessfully = false;

        try
        {
            // 2. Await startup history load if running, with bounded timeout
            if (_historyLoadTaskProvider != null)
            {
                try
                {
                    var loadTask = _historyLoadTaskProvider();
                    if (loadTask != null && !loadTask.IsCompleted)
                    {
                        using var loadCts = new CancellationTokenSource(timeout);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, loadCts.Token);
                        await loadTask.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Startup history wait error during shutdown: {ex}");
                }
            }

            // 3. Stop Process Monitoring coordinator with bounded timeout and await completion
            if (_stopMonitoringAction != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(timeout);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                    await _stopMonitoringAction(linked.Token).ConfigureAwait(false);
                    monitoringStoppedSuccessfully = true;
                }
                catch (OperationCanceledException)
                {
                    Trace.TraceError("Monitoring shutdown timed out or was cancelled.");
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Monitoring shutdown error: {ex}");
                }
            }
            else
            {
                monitoringStoppedSuccessfully = true;
            }

            // If monitoring timed out or failed to stop, do NOT proceed to dispose dependent services!
            // Retain required dependencies to prevent ObjectDisposedException and use-after-free
            // from active background tasks.
            if (!monitoringStoppedSuccessfully)
            {
                // Still attempt to persist settings before aborting dependency disposal
                try
                {
                    _settingsService.SaveSettings(_settingsService.CurrentSettings);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Settings persistence error on shutdown: {ex}");
                }

                return false;
            }

            // 4 & 5. Ensure all history-producing operations are stopped, then persist final history state
            if (_historyStore != null && _historyStore.IsPersistenceEnabled)
            {
                try
                {
                    using var cts = new CancellationTokenSource(timeout);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                    await _historyStore.PersistAsync(cancellationToken: linked.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"History store persistence error on shutdown: {ex}");
                }
            }

            // 6. Persist user settings and required application state
            try
            {
                _settingsService.SaveSettings(_settingsService.CurrentSettings);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Settings persistence error on shutdown: {ex}");
            }

            // 7. Dispose ViewModel hierarchy, coordinator, tray icon, and plugins in dependency-safe order
            if (_disposeAction != null)
            {
                try
                {
                    await _disposeAction().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Disposal error on shutdown: {ex}");
                }
            }

            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _shutdownState, 2);

            // 8. Complete UI shutdown
            try
            {
                _closeAction?.Invoke();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Close callback error: {ex}");
            }
        }
    }
}
