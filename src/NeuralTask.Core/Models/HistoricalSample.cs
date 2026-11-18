using System;
using System.Collections.Generic;

namespace NeuralTask.Core.Models;

/// <summary>
/// Compact per-process metrics captured within a historical telemetry tick.
/// Memory-budgeted to prevent unbounded memory growth during continuous retention.
/// </summary>
public record HistoricalProcessMetrics(
    int Pid,
    string Name,
    double CpuPercent,
    long RamBytes,
    double DiskBytesPerSec,
    double NetworkBytesPerSec,
    string? FlagReason = null
);

/// <summary>
/// Point-in-time system-wide and top-process telemetry snapshot stored in the rolling HistoryStore.
/// </summary>
public record HistoricalSample(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    double ProcessIoBytesPerSec,
    double NetworkReceivedBytesPerSec,
    double NetworkSentBytesPerSec,
    int ProcessCount,
    IReadOnlyList<HistoricalProcessMetrics> TopProcesses
);
