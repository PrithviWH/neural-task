using System;

namespace NeuralTask.Core.Models;

/// <summary>
/// Genuine system-wide telemetry captured directly from Windows kernel structures and interfaces.
/// </summary>
public record SystemMetricsSnapshot(
    double CpuPercent,
    long TotalMemoryBytes,
    long UsedMemoryBytes,
    long AvailableMemoryBytes,
    double MemoryPercent,
    double ProcessIoBytesPerSec,
    double NetworkReceivedBytesPerSec,
    double NetworkSentBytesPerSec,
    string ProcessorName,
    int LogicalCoreCount,
    string OsVersion,
    int ProcessCount,
    TimeSpan Uptime,
    double? CpuClockMhz = null,
    double? CpuPowerWatts = null,
    double? CpuTemperatureC = null
);
