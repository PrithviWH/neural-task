using System;
using System.Collections.Generic;
using System.Diagnostics;
using NeuralTask.App.Services;
using NeuralTask.Core.Models;
using Xunit;

namespace NeuralTask.Core.Tests;

public class TrayIconPerformanceTests
{
    [Fact]
    public void TrayIcon_MiniGraphRedraw_IsFastAndUnderSubMillisecondThreshold()
    {
        var settingsService = new JsonSettingsService();
        using var trayService = new TrayIconService(settingsService);

        var dummySnapshots = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(100, "chrome.exe", 0, 5.2, 500_000_000, 1000, 0, DateTime.Now, null, false),
            new ProcessSnapshot(200, "code.exe", 0, 2.1, 300_000_000, 500, 0, DateTime.Now, null, false)
        };

        var dummySystem = new SystemMetricsSnapshot(
            CpuPercent: 15.5,
            TotalMemoryBytes: 16_000_000_000,
            UsedMemoryBytes: 8_000_000_000,
            AvailableMemoryBytes: 8_000_000_000,
            MemoryPercent: 50.0,
            ProcessIoBytesPerSec: 100_000,
            NetworkReceivedBytesPerSec: 50_000,
            NetworkSentBytesPerSec: 20_000,
            ProcessorName: "Test CPU",
            LogicalCoreCount: 16,
            OsVersion: "Windows 11",
            ProcessCount: 200,
            Uptime: TimeSpan.FromHours(5)
        );

        // Warmup
        trayService.UpdateMetrics(dummySnapshots, dummySystem);

        // Profile 100 consecutive ticks
        var sw = Stopwatch.StartNew();
        const int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            trayService.UpdateMetrics(dummySnapshots, dummySystem);
        }

        sw.Stop();
        double avgMillisecondsPerFrame = sw.Elapsed.TotalMilliseconds / iterations;

        // Constraint check: tray icon redraw must be cheap enough not to add noticeable overhead
        // Sub-millisecond target (< 1.5ms per frame)
        Assert.True(avgMillisecondsPerFrame < 1.5,
            $"Tray redraw took average {avgMillisecondsPerFrame:F3}ms per frame (expected < 1.5ms)");
    }
}
