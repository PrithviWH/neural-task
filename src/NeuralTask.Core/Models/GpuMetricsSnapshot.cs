using System;
using System.Collections.Generic;

namespace NeuralTask.Core.Models;

public record GpuProcessMetric(int Pid, string Name, double GpuPercent);

public record GpuMetricsSnapshot(
    bool IsSupported,
    string AdapterName,
    double UtilizationPercent,
    long DedicatedMemoryUsedBytes,
    long DedicatedMemoryTotalBytes,
    IReadOnlyList<GpuProcessMetric> TopGpuProcesses
)
{
    public double DedicatedMemoryUsedGb => Math.Round(DedicatedMemoryUsedBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double DedicatedMemoryTotalGb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double DedicatedMemoryPercent =>
        DedicatedMemoryTotalBytes > 0
            ? Math.Clamp(Math.Round((double)DedicatedMemoryUsedBytes / DedicatedMemoryTotalBytes * 100.0, 1), 0.0, 100.0)
            : 0.0;

    public static readonly GpuMetricsSnapshot Unsupported = new(
        IsSupported: false,
        AdapterName: "No GPU detected",
        UtilizationPercent: 0.0,
        DedicatedMemoryUsedBytes: 0,
        DedicatedMemoryTotalBytes: 0,
        TopGpuProcesses: Array.Empty<GpuProcessMetric>()
    );
}
