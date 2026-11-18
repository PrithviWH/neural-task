using System;

namespace NeuralTask.Contracts;

/// <summary>
/// Immutable snapshot of kernel and hardware performance metrics passed
/// to plugins on each periodic monitoring tick.
/// </summary>
/// <param name="CpuUsagePercent">Total system CPU utilization percentage (0.0 to 100.0).</param>
/// <param name="TotalMemoryBytes">Total physical installed RAM in bytes.</param>
/// <param name="UsedMemoryBytes">Currently utilized physical RAM in bytes.</param>
/// <param name="AvailableMemoryBytes">Currently free/available physical RAM in bytes.</param>
/// <param name="ActiveProcessCount">Total number of active running processes on the system.</param>
/// <param name="Timestamp">Timestamp when the metrics were captured by the kernel monitor.</param>
public record PluginTelemetryContext(
    double CpuUsagePercent,
    long TotalMemoryBytes,
    long UsedMemoryBytes,
    long AvailableMemoryBytes,
    int ActiveProcessCount,
    DateTime Timestamp);
