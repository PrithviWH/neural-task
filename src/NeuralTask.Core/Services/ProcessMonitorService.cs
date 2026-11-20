using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Core implementation of IProcessMonitorService.
/// Computes normalized CPU %, Disk I/O, and Network I/O deltas without UI dependencies.
/// </summary>
public class ProcessMonitorService : IProcessMonitorService, IDisposable, IAsyncDisposable
{
    private const double HundredNanoSecondsPerSecond = 10_000_000.0;

    private readonly IProcessDataSampler _sampler;
    private readonly ITimeSource _timeSource;
    private readonly int _processorCount;
    private readonly ObservableSnapshotStream _stream = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _samplingExecutionLock = new(1, 1);

    private Dictionary<int, ProcessSampleHistory> _history = new();
    private IReadOnlyList<ProcessSnapshot> _currentSnapshot = Array.Empty<ProcessSnapshot>();

    public enum ServiceLifecycleState
    {
        Created,
        Running,
        Stopping,
        Stopped,
        Disposed
    }

    private ServiceLifecycleState _state = ServiceLifecycleState.Created;
    private CancellationTokenSource? _loopCts;
    private Task? _monitorLoopTask;
    private Task? _activeStoppingTask;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(1000);

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _state == ServiceLifecycleState.Running;
            }
        }
    }

    public ServiceLifecycleState LifecycleState
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public IObservable<IReadOnlyList<ProcessSnapshot>> Snapshots => _stream;

    public event EventHandler<IReadOnlyList<ProcessSnapshot>>? SnapshotsUpdated;

    public IReadOnlyList<ProcessSnapshot> CurrentSnapshot
    {
        get
        {
            lock (_stateLock)
            {
                return _currentSnapshot;
            }
        }
    }

    public ProcessMonitorService(
        IProcessDataSampler? sampler = null,
        ITimeSource? timeSource = null,
        int? processorCount = null)
    {
        _sampler = sampler ?? Win32ProcessDataSampler.Instance;
        _timeSource = timeSource ?? SystemTimeSource.Instance;
        _processorCount = processorCount ?? Math.Max(1, Environment.ProcessorCount);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state == ServiceLifecycleState.Disposed)
                throw new ObjectDisposedException(nameof(ProcessMonitorService));

            if (_state == ServiceLifecycleState.Running)
                return Task.CompletedTask;

            if (_state == ServiceLifecycleState.Stopping)
                throw new InvalidOperationException("Cannot start monitoring while service is in the process of stopping.");

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopCts = cts;
            _state = ServiceLifecycleState.Running;

            _monitorLoopTask = Task.Run(async () =>
            {
                try
                {
                    await MonitorLoopAsync(cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    lock (_stateLock)
                    {
                        if (_state == ServiceLifecycleState.Running)
                        {
                            _state = ServiceLifecycleState.Stopped;
                        }
                    }
                    cts.Dispose();
                }
            }, cts.Token);

            return Task.CompletedTask;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stoppingTask;

        lock (_stateLock)
        {
            if (_state == ServiceLifecycleState.Disposed ||
                _state == ServiceLifecycleState.Stopped ||
                _state == ServiceLifecycleState.Created)
            {
                return Task.CompletedTask;
            }

            if (_state == ServiceLifecycleState.Stopping && _activeStoppingTask != null)
            {
                // A stop is already in progress; reuse and await the existing stopping operation
                stoppingTask = _activeStoppingTask;
            }
            else
            {
                _state = ServiceLifecycleState.Stopping;

                try
                {
                    _loopCts?.Cancel();
                }
                catch (ObjectDisposedException) { }

                var loopTask = _monitorLoopTask;
                if (loopTask == null || loopTask.IsCompleted)
                {
                    _state = ServiceLifecycleState.Stopped;
                    _monitorLoopTask = null;
                    _loopCts = null;
                    return Task.CompletedTask;
                }

                _activeStoppingTask = stoppingTask = StopInternalAsync(loopTask);
            }
        }

        return AwaitStoppingTaskWithCallerCancellation(stoppingTask, cancellationToken);
    }

    private async Task StopInternalAsync(Task loopTask)
    {
        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown via _loopCts.Cancel()
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Process monitor loop faulted during shutdown: {ex}");
        }
        finally
        {
            lock (_stateLock)
            {
                if (_state == ServiceLifecycleState.Stopping)
                {
                    _state = ServiceLifecycleState.Stopped;
                }
                _monitorLoopTask = null;
                _loopCts = null;
                _activeStoppingTask = null;
            }
        }
    }

    private static async Task AwaitStoppingTaskWithCallerCancellation(Task stoppingTask, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            // If caller cancellation fires, WaitAsync throws OperationCanceledException.
            // This propagates to the caller without clearing _monitorLoopTask or _activeStoppingTask.
            // Ownership of the running background task is strictly preserved until it completes.
            await stoppingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await stoppingTask.ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ProcessSnapshot> SampleOnce() => SampleOnce(CancellationToken.None);

    public IReadOnlyList<ProcessSnapshot> SampleOnce(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            // Documented lifecycle policy:
            // Once shutdown begins (Stopping, Stopped, or Disposed), new sampling operations
            // are rejected and the last valid snapshot is returned immediately without acquiring
            // locks or accessing native Win32/WMI APIs.
            if (_state != ServiceLifecycleState.Running && _state != ServiceLifecycleState.Created)
            {
                return _currentSnapshot;
            }
        }

        if (cancellationToken.IsCancellationRequested) return _currentSnapshot;

        if (!_samplingExecutionLock.Wait(0))
        {
            // Prevent overlapping sampling passes
            return _currentSnapshot;
        }

        try
        {
            lock (_stateLock)
            {
                if (_state != ServiceLifecycleState.Running && _state != ServiceLifecycleState.Created)
                {
                    return _currentSnapshot;
                }
            }

            if (cancellationToken.IsCancellationRequested) return _currentSnapshot;

            var rawSamples = _sampler.CollectRawSamples();
            if (cancellationToken.IsCancellationRequested) return _currentSnapshot;

            var currentTicks = _timeSource.TimestampTicks;
            var ticksPerSecond = _timeSource.TicksPerSecond;

            var snapshots = new List<ProcessSnapshot>(rawSamples.Count);
            var newHistory = new Dictionary<int, ProcessSampleHistory>(rawSamples.Count);

            lock (_stateLock)
            {
                if (_state == ServiceLifecycleState.Disposed) return _currentSnapshot;

                foreach (var raw in rawSamples)
                {
                    if (raw.IsAccessDenied)
                    {
                        snapshots.Add(new ProcessSnapshot(
                            Pid: raw.Pid,
                            Name: raw.Name,
                            ParentPid: raw.ParentPid,
                            CpuPercent: 0.0,
                            RamBytes: raw.WorkingSetBytes,
                            DiskBytesPerSec: 0.0,
                            NetworkBytesPerSec: null,
                            StartTime: raw.StartTime,
                            ExecutablePath: raw.ExecutablePath,
                            IsAccessDenied: true,
                            HandleCount: raw.HandleCount));

                        continue;
                    }

                    var totalCpuTicks = raw.KernelTimeTicks + raw.UserTimeTicks;
                    var totalDiskBytes = raw.ReadTransferBytes + raw.WriteTransferBytes;
                    // Note: IO_COUNTERS.OtherTransferCount measures non-read/non-write control and device I/O (IOCTLs, named pipes).
                    // It does NOT represent network transfer. Per-process network throughput is unavailable via IO_COUNTERS.
                    double? netBytesPerSec = null;

                    double cpuPercent = 0.0;
                    double diskBytesPerSec = 0.0;

                    if (_history.TryGetValue(raw.Pid, out var prev))
                    {
                        var deltaTicks = currentTicks - prev.TimestampTicks;
                        var deltaSeconds = deltaTicks / ticksPerSecond;

                        if (deltaSeconds > 0.0001)
                        {
                            var deltaCpuTicks = totalCpuTicks - prev.TotalCpuTicks;
                            if (deltaCpuTicks > 0)
                            {
                                var cpuSecondsConsumed = deltaCpuTicks / HundredNanoSecondsPerSecond;
                                var rawCpuPercent = (cpuSecondsConsumed / (deltaSeconds * _processorCount)) * 100.0;
                                cpuPercent = Math.Clamp(Math.Round(rawCpuPercent, 2), 0.0, 100.0);
                            }

                            if (totalDiskBytes >= prev.DiskBytes)
                            {
                                var deltaDisk = totalDiskBytes - prev.DiskBytes;
                                diskBytesPerSec = Math.Round(deltaDisk / deltaSeconds, 2);
                            }
                        }
                    }

                    newHistory[raw.Pid] = new ProcessSampleHistory(
                        TimestampTicks: currentTicks,
                        TotalCpuTicks: totalCpuTicks,
                        DiskBytes: totalDiskBytes,
                        NetworkBytes: 0);

                    snapshots.Add(new ProcessSnapshot(
                        Pid: raw.Pid,
                        Name: raw.Name,
                        ParentPid: raw.ParentPid,
                        CpuPercent: cpuPercent,
                        RamBytes: raw.WorkingSetBytes,
                        DiskBytesPerSec: diskBytesPerSec,
                        NetworkBytesPerSec: netBytesPerSec,
                        StartTime: raw.StartTime,
                        ExecutablePath: raw.ExecutablePath,
                        IsAccessDenied: false,
                        HandleCount: raw.HandleCount));
                }

                // Replace history dictionary (evicts terminated processes, preventing memory leaks)
                _history = newHistory;
                _currentSnapshot = snapshots;
            }

            bool shouldPublish;
            lock (_stateLock)
            {
                shouldPublish = (_state == ServiceLifecycleState.Running || _state == ServiceLifecycleState.Created) && !cancellationToken.IsCancellationRequested;
            }

            if (shouldPublish)
            {
                _stream.Publish(snapshots);
                SnapshotsUpdated?.Invoke(this, snapshots);
            }

            return snapshots;
        }
        finally
        {
            _samplingExecutionLock.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        // Initial prime sample so the subsequent sample can compute deltas immediately
        try
        {
            SampleOnce(ct);
        }
        catch
        {
            // Never crash background monitoring thread
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;
                SampleOnce(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Silently recover on transient OS query errors
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? taskToAwait;
        lock (_stateLock)
        {
            if (_state == ServiceLifecycleState.Disposed) return;
            _state = ServiceLifecycleState.Disposed;

            try
            {
                _loopCts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            taskToAwait = _activeStoppingTask ?? _monitorLoopTask;
        }

        // 1. Await the monitoring loop if active
        if (taskToAwait != null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Trace.TraceError($"Error awaiting monitor loop on disposal: {ex}");
            }
        }

        // 2. Await active native sampling pass before disposing dependent resources.
        // Direct asynchronous wait without check-then-act or fire-and-forget tasks!
        await _samplingExecutionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Dispose();
        }
        finally
        {
            _samplingExecutionLock.Release();
            _samplingExecutionLock.Dispose();
        }
    }

    public void Dispose()
    {
        Task? taskToAwait;
        lock (_stateLock)
        {
            if (_state == ServiceLifecycleState.Disposed) return;
            _state = ServiceLifecycleState.Disposed;

            try
            {
                _loopCts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            taskToAwait = _activeStoppingTask ?? _monitorLoopTask;
        }

        if (taskToAwait != null && !taskToAwait.IsCompleted)
        {
            try
            {
                taskToAwait.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        // Synchronous bounded wait for active native sampling to complete.
        // Policy: If native sampling cannot be interrupted and does not complete within 2 seconds,
        // we log a warning and do NOT dispose the semaphore or stream out from under the running thread,
        // avoiding ObjectDisposedException or use-after-free corruption.
        bool acquired = false;
        try
        {
            acquired = _samplingExecutionLock.Wait(TimeSpan.FromSeconds(2));
        }
        catch (ObjectDisposedException) { }

        if (acquired)
        {
            try
            {
                _stream.Dispose();
            }
            finally
            {
                _samplingExecutionLock.Release();
                _samplingExecutionLock.Dispose();
            }
        }
        else
        {
            Trace.TraceWarning("ProcessMonitorService: Sampling operation did not complete within disposal timeout. Resources will remain allocated to prevent use-after-free.");
        }
    }

    private record ProcessSampleHistory(
        long TimestampTicks,
        long TotalCpuTicks,
        ulong DiskBytes,
        ulong NetworkBytes);
}
