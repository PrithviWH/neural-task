using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class DashboardHistoryScrubberTests
{
    private static SystemMetricsSnapshot CreateSystemMetrics(double cpu, double ramPercent)
    {
        return new SystemMetricsSnapshot(
            CpuPercent: cpu,
            TotalMemoryBytes: 16_000_000_000,
            UsedMemoryBytes: (long)(16_000_000_000 * (ramPercent / 100.0)),
            AvailableMemoryBytes: (long)(16_000_000_000 * (1.0 - ramPercent / 100.0)),
            MemoryPercent: ramPercent,
            ProcessIoBytesPerSec: 1024 * 1024,
            NetworkReceivedBytesPerSec: 50 * 1024,
            NetworkSentBytesPerSec: 25 * 1024,
            ProcessorName: "Ryzen Test",
            LogicalCoreCount: 16,
            OsVersion: "Windows 11",
            ProcessCount: 120,
            Uptime: TimeSpan.FromHours(2)
        );
    }

    private static IReadOnlyList<ProcessSnapshot> CreateProcesses(double topCpu)
    {
        return new List<ProcessSnapshot>
        {
            new ProcessSnapshot(1001, "heavy_task.exe", 1000, topCpu, 500_000_000, 1000, 500, DateTime.Now, @"C:\heavy.exe", false),
            new ProcessSnapshot(1002, "light_task.exe", 1000, 1.0, 50_000_000, 100, 50, DateTime.Now, @"C:\light.exe", false)
        };
    }

    [Fact]
    public void DashboardViewModel_LiveUpdates_BufferHistoryAndExposeTimelineRange()
    {
        var historyStore = new HistoryStore();
        var exportService = new ExportService();
        var vm = new DashboardViewModel(historyStore, exportService);

        Assert.False(vm.HasHistoricalSamples);

        vm.ApplyMetrics(CreateProcesses(15.0), CreateSystemMetrics(cpu: 15.0, ramPercent: 30.0));
        Assert.False(vm.HasHistoricalSamples); // Need > 1 for scrubbing range

        vm.ApplyMetrics(CreateProcesses(45.0), CreateSystemMetrics(cpu: 45.0, ramPercent: 60.0));
        Assert.True(vm.HasHistoricalSamples);
        Assert.Equal(2, historyStore.SampleCount);
        Assert.True(vm.IsLive);
        Assert.Equal(1.0, vm.ScrubberValue);
        Assert.Equal("45.0%", vm.CpuDisplay);
    }

    [Fact]
    public void DashboardViewModel_Scrubbing_DisplaysHistoricalTelemetry()
    {
        var historyStore = new HistoryStore();
        var exportService = new ExportService();
        var vm = new DashboardViewModel(historyStore, exportService);

        // Sample 1: Low load
        vm.ApplyMetrics(CreateProcesses(5.0), CreateSystemMetrics(cpu: 5.0, ramPercent: 20.0));
        // Sample 2: Mid load
        vm.ApplyMetrics(CreateProcesses(50.0), CreateSystemMetrics(cpu: 50.0, ramPercent: 50.0));
        // Sample 3: High load
        vm.ApplyMetrics(CreateProcesses(95.0), CreateSystemMetrics(cpu: 95.0, ramPercent: 90.0));

        Assert.Equal("95.0%", vm.CpuDisplay);

        // Scrub back to the oldest sample (0.0)
        vm.ScrubberValue = 0.0;

        Assert.True(vm.IsScrubbing);
        Assert.False(vm.IsLive);
        Assert.Equal("5.0%", vm.CpuDisplay);
        Assert.Equal(20.0, vm.RamPercent);
        Assert.Contains("Scrubbing", vm.TimelineTimeDisplay);
        Assert.Contains("Historical", vm.StatusPillText);

        // Top offender should reflect historical sample
        Assert.Equal("heavy_task.exe", vm.TopCpuProcesses[0].Name);
        Assert.Equal("5.00%", vm.TopCpuProcesses[0].ValueDisplay);

        // Live ticks while scrubbing should NOT overwrite scrubbed displays
        vm.ApplyMetrics(CreateProcesses(99.0), CreateSystemMetrics(cpu: 99.0, ramPercent: 99.0));
        Assert.Equal("5.0%", vm.CpuDisplay); // Still historical!
    }

    [Fact]
    public void DashboardViewModel_ResumeLive_RestoresCurrentStreamingMetrics()
    {
        var historyStore = new HistoryStore();
        var exportService = new ExportService();
        var vm = new DashboardViewModel(historyStore, exportService);

        vm.ApplyMetrics(CreateProcesses(10.0), CreateSystemMetrics(cpu: 10.0, ramPercent: 30.0));
        vm.ApplyMetrics(CreateProcesses(80.0), CreateSystemMetrics(cpu: 80.0, ramPercent: 75.0));

        // Scrub back
        vm.ScrubberValue = 0.0;
        Assert.True(vm.IsScrubbing);
        Assert.Equal("10.0%", vm.CpuDisplay);

        // Resume Live
        vm.ResumeLiveCommand.Execute(null);

        Assert.False(vm.IsScrubbing);
        Assert.True(vm.IsLive);
        Assert.Equal(1.0, vm.ScrubberValue);
        Assert.Equal("80.0%", vm.CpuDisplay);
        Assert.Equal("Live • Streaming", vm.TimelineTimeDisplay);
    }
}
