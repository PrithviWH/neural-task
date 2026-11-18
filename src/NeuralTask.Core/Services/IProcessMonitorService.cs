using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Headless service interface for monitoring running system processes in real-time.
/// </summary>
public interface IProcessMonitorService : IDisposable
{
    /// <summary>
    /// Reactive stream of process snapshots emitted on each sampling tick.
    /// </summary>
    IObservable<IReadOnlyList<ProcessSnapshot>> Snapshots { get; }

    /// <summary>
    /// Standard .NET event fired whenever a new snapshot is calculated.
    /// </summary>
    event EventHandler<IReadOnlyList<ProcessSnapshot>>? SnapshotsUpdated;

    /// <summary>
    /// The most recent snapshot list available.
    /// </summary>
    IReadOnlyList<ProcessSnapshot> CurrentSnapshot { get; }

    /// <summary>
    /// Configurable sampling interval (defaults to 1000ms).
    /// </summary>
    TimeSpan RefreshInterval { get; set; }

    /// <summary>
    /// Whether the background monitoring loop is actively polling.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the background monitoring loop.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background monitoring loop.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Executes a single sampling pass immediately and returns the resulting snapshots.
    /// </summary>
    IReadOnlyList<ProcessSnapshot> SampleOnce();
}
