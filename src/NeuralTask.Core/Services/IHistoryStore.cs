using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service managing a rolling in-memory window of historical system and top-process telemetry.
/// Supports smooth timeline scrubbing and optional restart persistence without network access.
/// </summary>
public interface IHistoryStore
{
    /// <summary>
    /// Rolling retention duration (default 30 minutes, configurable up to 60+ minutes).
    /// </summary>
    TimeSpan RetentionWindow { get; set; }

    /// <summary>
    /// Current number of historical samples retained in memory.
    /// </summary>
    int SampleCount { get; }

    /// <summary>
    /// Whether historical samples should be persisted to disk across application restarts.
    /// </summary>
    bool IsPersistenceEnabled { get; set; }

    /// <summary>
    /// Adds a live telemetry tick into the rolling buffer, capturing system metrics and top offenders.
    /// Automatically trims samples older than the retention window.
    /// </summary>
    void AddSample(
        DateTimeOffset timestamp,
        SystemMetricsSnapshot systemMetrics,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null);

    /// <summary>
    /// Retrieves all retained samples in chronological order.
    /// </summary>
    IReadOnlyList<HistoricalSample> GetHistory();

    /// <summary>
    /// Retrieves the historical sample closest to the specified timestamp.
    /// </summary>
    HistoricalSample? GetSampleAt(DateTimeOffset timestamp);

    /// <summary>
    /// Retrieves the historical sample at the given normalized timeline position (0.0 = oldest, 1.0 = newest).
    /// </summary>
    HistoricalSample? GetSampleByFraction(double fraction);

    /// <summary>
    /// Retrieves the most recent historical sample.
    /// </summary>
    HistoricalSample? GetLatest();

    /// <summary>
    /// Clears all historical samples from memory.
    /// </summary>
    void Clear();

    /// <summary>
    /// Persists retained samples to a local-only JSON file if persistence is enabled.
    /// </summary>
    Task PersistAsync(string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads previously persisted samples from a local-only JSON file.
    /// </summary>
    Task<bool> LoadPersistedAsync(string? filePath = null, CancellationToken cancellationToken = default);
}
