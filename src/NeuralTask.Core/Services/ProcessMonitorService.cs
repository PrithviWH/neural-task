using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Core implementation of IProcessMonitorService.
/// Computes normalized CPU %, Disk I/O, and Network I/O deltas without UI dependencies.
/// </summary>
public class ProcessMonitorService : IProcessMonitorService
{
    private const double HundredNanoSecondsPerSecond = 10_000_000.0;

    private readonly IProcessDataSampler _sampler;
    private readonly ITimeSource _timeSource;
    private readonly int _processorCount;
    private readonly ObservableSnapshotStream _stream = new();
    private readonly object _stateLock = new();

    private Dictionary<int, ProcessSampleHistory> _history = new();
    private IReadOnlyList<ProcessSnapshot> _currentSnapshot = Array.Empty<ProcessSnapshot>();

    private CancellationTokenSource? _cts;
    private Task? _monitorLoopTask;
    private bool _isDisposed;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(1000);

    public bool IsRunning { get; private set; }

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
            if (_isDisposed) throw new ObjectDisposedException(nameof(ProcessMonitorService));
            if (IsRunning) return Task.CompletedTask;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            _monitorLoopTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? ctsToCancel;
        Task? taskToAwait;

        lock (_stateLock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            ctsToCancel = _cts;
            taskToAwait = _monitorLoopTask;
            _cts = null;
            _monitorLoopTask = null;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (taskToAwait != null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        ctsToCancel?.Dispose();
    }

    public IReadOnlyList<ProcessSnapshot> SampleOnce()
    {
        var rawSamples = _sampler.CollectRawSamples();
        var currentTicks = _timeSource.TimestampTicks;
        var ticksPerSecond = _timeSource.TicksPerSecond;

        var snapshots = new List<ProcessSnapshot>(rawSamples.Count);
        var newHistory = new Dictionary<int, ProcessSampleHistory>(rawSamples.Count);

        lock (_stateLock)
        {
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
                        NetworkBytesPerSec: 0.0,
                        StartTime: raw.StartTime,
                        ExecutablePath: raw.ExecutablePath,
                        IsAccessDenied: true,
                        HandleCount: raw.HandleCount));

                    continue;
                }

                var totalCpuTicks = raw.KernelTimeTicks + raw.UserTimeTicks;
                var totalDiskBytes = raw.ReadTransferBytes + raw.WriteTransferBytes;
                var totalNetBytes = raw.OtherTransferBytes;

                double cpuPercent = 0.0;
                double diskBytesPerSec = 0.0;
                double netBytesPerSec = 0.0;

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

                        if (totalNetBytes >= prev.NetworkBytes)
                        {
                            var deltaNet = totalNetBytes - prev.NetworkBytes;
                            netBytesPerSec = Math.Round(deltaNet / deltaSeconds, 2);
                        }
                    }
                }

                newHistory[raw.Pid] = new ProcessSampleHistory(
                    TimestampTicks: currentTicks,
                    TotalCpuTicks: totalCpuTicks,
                    DiskBytes: totalDiskBytes,
                    NetworkBytes: totalNetBytes);

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

        // Notify observers and event listeners
        _stream.Publish(snapshots);
        SnapshotsUpdated?.Invoke(this, snapshots);

        return snapshots;
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        // Initial prime sample so the subsequent sample can compute deltas immediately
        try
        {
            SampleOnce();
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
                SampleOnce();
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

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _stream.Dispose();
    }

    private record ProcessSampleHistory(
        long TimestampTicks,
        long TotalCpuTicks,
        ulong DiskBytes,
        ulong NetworkBytes);
}
