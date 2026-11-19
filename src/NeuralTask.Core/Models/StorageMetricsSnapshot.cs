using System;
using System.Collections.Generic;

namespace NeuralTask.Core.Models;

public record DriveItemMetric(
    string Name,
    string Label,
    long TotalBytes,
    long UsedBytes,
    long FreeBytes,
    double PercentUsed
)
{
    public double TotalGb => Math.Round(TotalBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double UsedGb => Math.Round(UsedBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double FreeGb => Math.Round(FreeBytes / (1024.0 * 1024.0 * 1024.0), 1);
}

public record StorageMetricsSnapshot(
    DriveItemMetric? PrimaryDrive,
    IReadOnlyList<DriveItemMetric> AllDrives,
    long TotalCapacityBytes,
    long TotalUsedBytes,
    double TotalPercentUsed
)
{
    public double TotalCapacityGb => Math.Round(TotalCapacityBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double TotalUsedGb => Math.Round(TotalUsedBytes / (1024.0 * 1024.0 * 1024.0), 1);
}
