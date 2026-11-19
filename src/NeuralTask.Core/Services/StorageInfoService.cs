using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public class StorageInfoService : IStorageInfoService
{
    public static readonly StorageInfoService Instance = new();

    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private StorageMetricsSnapshot? _cachedSnapshot;
    private readonly object _lock = new();

    public StorageMetricsSnapshot GetStorageSnapshot()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.Now;
            // Cache for 5 seconds since disk capacities do not rapidly change
            if (_cachedSnapshot != null && (now - _lastCheck).TotalSeconds < 5)
            {
                return _cachedSnapshot;
            }

            var drives = new List<DriveItemMetric>();
            long totalCap = 0;
            long totalUsed = 0;
            DriveItemMetric? primary = null;

            try
            {
                var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
                        {
                            long free = d.TotalFreeSpace;
                            long total = d.TotalSize;
                            long used = Math.Max(0, total - free);
                            double pct = Math.Clamp(Math.Round(((double)used / total) * 100.0, 1), 0.0, 100.0);
                            string label = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : (d.Name.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase) ? "System OS" : "Local Disk");

                            var item = new DriveItemMetric(
                                Name: d.Name,
                                Label: label,
                                TotalBytes: total,
                                UsedBytes: used,
                                FreeBytes: free,
                                PercentUsed: pct
                            );

                            drives.Add(item);
                            totalCap += total;
                            totalUsed += used;

                            if (d.Name.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                primary = item;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            primary ??= drives.FirstOrDefault();
            double overallPct = totalCap > 0 ? Math.Clamp(Math.Round(((double)totalUsed / totalCap) * 100.0, 1), 0.0, 100.0) : 0.0;

            _cachedSnapshot = new StorageMetricsSnapshot(
                PrimaryDrive: primary,
                AllDrives: drives,
                TotalCapacityBytes: totalCap,
                TotalUsedBytes: totalUsed,
                TotalPercentUsed: overallPct
            );

            _lastCheck = now;
            return _cachedSnapshot;
        }
    }
}
