using System;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// Snapshot of GPU hardware acceleration and video memory metrics.
/// </summary>
public record GpuMetrics(
    string AdapterName,
    double UtilizationPercent,
    long DedicatedMemoryUsedBytes,
    long DedicatedMemoryTotalBytes,
    long SharedMemoryUsedBytes,
    double Engine3DPercent,
    double EngineVideoDecodePercent,
    double EngineCopyPercent,
    double EngineComputePercent,
    DateTime Timestamp)
{
    public double DedicatedMemoryUsedMb => Math.Round(DedicatedMemoryUsedBytes / (1024.0 * 1024.0), 1);
    public double DedicatedMemoryTotalMb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0), 1);
    public double SharedMemoryUsedMb => Math.Round(SharedMemoryUsedBytes / (1024.0 * 1024.0), 1);

    public double DedicatedMemoryPercent =>
        DedicatedMemoryTotalBytes > 0
            ? Math.Clamp(Math.Round((double)DedicatedMemoryUsedBytes / DedicatedMemoryTotalBytes * 100.0, 1), 0.0, 100.0)
            : 0.0;
}
