using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.Services;

public record ProcessMonitoringUpdate(
    IReadOnlyList<ProcessSnapshot> Processes,
    SystemMetricsSnapshot SystemMetrics
);

public class ProcessMonitorCoordinator : IDisposable, IAsyncDisposable
{
    private readonly IProcessMonitorService _monitorService;
    private readonly ISystemMetricsService _systemMetricsService;
    private readonly Dispatcher _dispatcher;

    private IDisposable? _subscription;
    private int _isProcessingUiUpdate = 0;
    private bool _isDisposed = false;

    public event Action<ProcessMonitoringUpdate>? UpdateReceived;

    public TimeSpan RefreshInterval
    {
        get => _monitorService.RefreshInterval;
        set => _monitorService.RefreshInterval = value;
    }

    public bool IsRunning => _monitorService.IsRunning;

    public ProcessMonitorCoordinator(
        IProcessMonitorService monitorService,
        ISystemMetricsService systemMetricsService,
        Dispatcher? dispatcher = null)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _systemMetricsService = systemMetricsService ?? throw new ArgumentNullException(nameof(systemMetricsService));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ProcessMonitorCoordinator));

        if (_subscription == null)
        {
            _subscription = _monitorService.Snapshots.Subscribe(new SnapshotObserver(OnSnapshotReceived));
        }

        await _monitorService.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _subscription?.Dispose();
        _subscription = null;

        await _monitorService.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetInterval(TimeSpan interval)
    {
        _monitorService.RefreshInterval = interval;
    }

    private void OnSnapshotReceived(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        if (_isDisposed) return;

        // 1. Process system-wide metrics on the monitoring background thread (zero UI blocking)
        SystemMetricsSnapshot systemMetrics;
        try
        {
            systemMetrics = _systemMetricsService.CollectSnapshot(snapshots);
        }
        catch
        {
            return;
        }

        var update = new ProcessMonitoringUpdate(snapshots, systemMetrics);

        // 2. Thread-Safe Dispatcher Queue Throttling:
        // If UI is still busy rendering the previous frame, skip this sample
        // to prevent Dispatcher queue buildup and memory pressure.
        if (Interlocked.CompareExchange(ref _isProcessingUiUpdate, 1, 0) == 0)
        {
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!_isDisposed)
                        {
                            UpdateReceived?.Invoke(update);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isProcessingUiUpdate, 0);
                    }
                });
            }
            catch
            {
                Interlocked.Exchange(ref _isProcessingUiUpdate, 0);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UpdateReceived = null;
        _subscription?.Dispose();
        _subscription = null;

        try
        {
            await _monitorService.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UpdateReceived = null;
        _subscription?.Dispose();
        _subscription = null;

        try
        {
            _monitorService.Dispose();
        }
        catch
        {
        }
    }

    private class SnapshotObserver : IObserver<IReadOnlyList<ProcessSnapshot>>
    {
        private readonly Action<IReadOnlyList<ProcessSnapshot>> _onNext;

        public SnapshotObserver(Action<IReadOnlyList<ProcessSnapshot>> onNext)
        {
            _onNext = onNext;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(IReadOnlyList<ProcessSnapshot> value) => _onNext(value);
    }
}
