using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class DashboardIntelligenceTests
{
    [Fact]
    public void GpuMetricsSnapshot_CalculatesMemoryPercentagesAccurately()
    {
        var processes = new List<GpuProcessMetric>
        {
            new(101, "render.exe", 45.2),
            new(202, "game.exe", 22.8)
        };

        var snapshot = new GpuMetricsSnapshot(
            IsSupported: true,
            AdapterName: "NVIDIA GeForce RTX 4080",
            UtilizationPercent: 68.0,
            DedicatedMemoryUsedBytes: 4L * 1024 * 1024 * 1024, // 4 GB
            DedicatedMemoryTotalBytes: 16L * 1024 * 1024 * 1024, // 16 GB
            TopGpuProcesses: processes);

        Assert.True(snapshot.IsSupported);
        Assert.Equal("NVIDIA GeForce RTX 4080", snapshot.AdapterName);
        Assert.Equal(4.0, snapshot.DedicatedMemoryUsedGb, 1);
        Assert.Equal(16.0, snapshot.DedicatedMemoryTotalGb, 1);
        Assert.Equal(25.0, snapshot.DedicatedMemoryPercent, 1);
        Assert.Equal(2, snapshot.TopGpuProcesses.Count);
        Assert.Equal("render.exe", snapshot.TopGpuProcesses[0].Name);
        Assert.Equal(45.2, snapshot.TopGpuProcesses[0].GpuPercent);
    }

    [Fact]
    public void GpuMetricsSnapshot_Unsupported_HasSafeDefaults()
    {
        var unsupported = GpuMetricsSnapshot.Unsupported;

        Assert.False(unsupported.IsSupported);
        Assert.Equal("No GPU detected", unsupported.AdapterName);
        Assert.Equal(0.0, unsupported.UtilizationPercent);
        Assert.Equal(0.0, unsupported.DedicatedMemoryPercent);
        Assert.Empty(unsupported.TopGpuProcesses);
    }

    [Fact]
    public void StorageInfoService_SamplesRealDrives()
    {
        var service = StorageInfoService.Instance;
        var snapshot = service.GetStorageSnapshot();

        Assert.NotNull(snapshot);
        // On any Windows workstation, at least one drive (typically C:\) is mounted
        Assert.True(snapshot.AllDrives.Count >= 1);
        Assert.True(snapshot.TotalCapacityBytes > 0);
        Assert.True(snapshot.TotalUsedBytes >= 0);
        Assert.InRange(snapshot.TotalPercentUsed, 0.0, 100.0);

        var primary = snapshot.PrimaryDrive;
        Assert.NotNull(primary);
        Assert.False(string.IsNullOrWhiteSpace(primary.Name));
        Assert.True(primary.TotalBytes > 0);
    }

    [Fact]
    public void NetworkInfoService_SamplesActiveAdapter()
    {
        var service = NetworkInfoService.Instance;
        var snapshot = service.GetPrimaryAdapterInfo();

        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Name));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.InterfaceTypeDisplay));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.LinkSpeedDisplay));
    }

    [Fact]
    public void SystemHealthEvaluator_AssessesHealthStatesAccurately()
    {
        // 1. Optimal State: Low CPU, Low RAM
        var sysOptimal = new SystemMetricsSnapshot(
            CpuPercent: 22.0,
            TotalMemoryBytes: 32L * 1024 * 1024 * 1024,
            UsedMemoryBytes: 8L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 24L * 1024 * 1024 * 1024,
            MemoryPercent: 25.0,
            ProcessIoBytesPerSec: 1024 * 1024,
            NetworkReceivedBytesPerSec: 50000,
            NetworkSentBytesPerSec: 25000,
            ProcessorName: "AMD Ryzen 9 7950X",
            LogicalCoreCount: 16,
            OsVersion: "Windows 11",
            ProcessCount: 150,
            Uptime: TimeSpan.FromHours(5));

        var optimalSnapshot = SystemHealthEvaluator.Evaluate(
            system: sysOptimal,
            gpu: null,
            peakCpu: 25.0,
            peakRam: 26.0,
            startupAppCount: 0,
            startupAverageScore: 0);

        Assert.Equal(HealthState.Optimal, optimalSnapshot.HealthState);
        Assert.Equal("Optimal", optimalSnapshot.HealthStateDisplay);
        Assert.Contains("16 Cores", optimalSnapshot.HardwareSummary);
        Assert.Contains("No high-impact startup", optimalSnapshot.StartupImpactSummary);

        // 2. Elevated State: CPU 75%
        var sysElevated = sysOptimal with { CpuPercent = 75.0, MemoryPercent = 50.0 };
        var elevatedSnapshot = SystemHealthEvaluator.Evaluate(
            system: sysElevated,
            gpu: null,
            peakCpu: 78.0,
            peakRam: 52.0,
            startupAppCount: 2,
            startupAverageScore: 45);

        Assert.Equal(HealthState.Elevated, elevatedSnapshot.HealthState);
        Assert.Equal("Elevated", elevatedSnapshot.HealthStateDisplay);
        Assert.Contains("2 startup apps", elevatedSnapshot.StartupImpactSummary);

        // 3. High Load State: CPU 94%
        var sysHighLoad = sysOptimal with { CpuPercent = 94.0, MemoryPercent = 93.0 };
        var highLoadSnapshot = SystemHealthEvaluator.Evaluate(
            system: sysHighLoad,
            gpu: null,
            peakCpu: 95.0,
            peakRam: 94.0,
            startupAppCount: 5,
            startupAverageScore: 80);

        Assert.Equal(HealthState.HeavyLoad, highLoadSnapshot.HealthState);
        Assert.Equal("High Load", highLoadSnapshot.HealthStateDisplay);
        Assert.Contains("Peak CPU: 95.0%", highLoadSnapshot.RecentSpikesSummary);
    }

    [Fact]
    public void PerformanceHistoryBuffer_CalculatesPeakAndGeneratesSmoothCurves()
    {
        var buffer = new PerformanceHistoryBuffer(maxSamples: 60);
        buffer.AddSample(10.0);
        buffer.AddSample(45.0);
        buffer.AddSample(88.5);
        buffer.AddSample(30.0);

        // Verify peak calculation
        Assert.Equal(88.5, buffer.GetPeak());

        // Generate smooth curve and area geometries
        var curveGeometry = buffer.GenerateSmoothCurveGeometry(600, 160, 100.0);
        var areaGeometry = buffer.GenerateSmoothAreaGeometry(600, 160, 100.0);

        Assert.NotNull(curveGeometry);
        Assert.True(curveGeometry.Figures.Count > 0);
        Assert.False(curveGeometry.Figures[0].IsClosed);

        Assert.NotNull(areaGeometry);
        Assert.True(areaGeometry.Figures.Count > 0);
        Assert.True(areaGeometry.Figures[0].IsClosed);
    }

    [Fact]
    public void PerformanceHistoryBuffer_DelayBuffer_MaintainsContinuousRealDataBeyondViewport()
    {
        var buffer = new PerformanceHistoryBuffer(maxSamples: 64);
        // Add 64 distinct increasing samples
        for (int i = 1; i <= 64; i++)
        {
            buffer.AddSample(i);
        }

        Assert.Equal(64, buffer.Count);
        Assert.Equal(64.0, buffer.GetLatest());
        Assert.Equal(1.0, buffer.GetMinimum());
        Assert.Equal(64.0, buffer.GetPeak());

        // Generate geometry across a 600px width
        var curveGeometry = buffer.GenerateSmoothCurveGeometry(600, 160, 100.0);
        Assert.NotNull(curveGeometry);
        Assert.Single(curveGeometry.Figures);

        // Add 65th sample to simulate next 1-second arrival
        buffer.AddSample(65.0);
        Assert.Equal(64, buffer.Count);
        Assert.Equal(65.0, buffer.GetLatest());
        Assert.Equal(2.0, buffer.GetMinimum());
    }

    [Fact]
    public void DashboardViewModel_SelectsAllFiveMetricsAndTimeRanges()
    {
        var vm = new DashboardViewModel();

        // 1. Verify default metric is Overview
        Assert.Equal("Overview", vm.SelectedMetric);

        // 2. Select CPU
        vm.SelectMetricCommand.Execute("Cpu");
        Assert.Equal("Cpu", vm.SelectedMetric);

        // 3. Select Memory
        vm.SelectMetricCommand.Execute("Memory");
        Assert.Equal("Memory", vm.SelectedMetric);

        // 4. Select GPU
        vm.SelectMetricCommand.Execute("Gpu");
        Assert.Equal("Gpu", vm.SelectedMetric);

        // 5. Select Disk
        vm.SelectMetricCommand.Execute("Disk");
        Assert.Equal("Disk", vm.SelectedMetric);

        // 6. Select Network
        vm.SelectMetricCommand.Execute("Network");
        Assert.Equal("Network", vm.SelectedMetric);

        // 7. Test Range Selection
        Assert.Equal("60s", vm.SelectedRange);
        vm.SelectRangeCommand.Execute("5m");
        Assert.Equal("5m", vm.SelectedRange);
        vm.SelectRangeCommand.Execute("30m");
        Assert.Equal("30m", vm.SelectedRange);
    }

    [Fact]
    public void DashboardViewModel_DisplaysTopConsumers()
    {
        var vm = new DashboardViewModel();

        var processes = new List<ProcessSnapshot>
        {
            new(100, "dwm.exe", 0, 1.5, 120 * 1024 * 1024, 0, 0, null, null, false),
            new(200, "chrome.exe", 0, 8.0, 800 * 1024 * 1024, 0, 0, null, null, false),
            new(300, "code.exe", 0, 4.0, 400 * 1024 * 1024, 0, 0, null, null, false),
            new(400, "spotify.exe", 0, 0.5, 200 * 1024 * 1024, 0, 0, null, null, false),
            new(500, "terminal.exe", 0, 0.2, 50 * 1024 * 1024, 0, 0, null, null, false)
        };

        var system = new SystemMetricsSnapshot(
            CpuPercent: 15.0,
            TotalMemoryBytes: 32L * 1024 * 1024 * 1024,
            UsedMemoryBytes: 8L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 24L * 1024 * 1024 * 1024,
            MemoryPercent: 25.0,
            ProcessIoBytesPerSec: 1024 * 1024,
            NetworkReceivedBytesPerSec: 50000,
            NetworkSentBytesPerSec: 25000,
            ProcessorName: "Test CPU",
            LogicalCoreCount: 8,
            OsVersion: "Windows 11 Pro",
            ProcessCount: 150,
            Uptime: TimeSpan.FromHours(10));

        vm.ApplyMetrics(processes, system);

        // Top 5 slots should exist without crashing and have bound view models
        Assert.Equal(5, vm.TopCpuProcesses.Count);
        Assert.Equal(5, vm.TopRamProcesses.Count);
        Assert.Equal(5, vm.TopGpuProcesses.Count);

        Assert.Equal("chrome.exe", vm.TopCpuProcesses[0].Name);
        Assert.Equal(8.0, vm.TopCpuProcesses[0].PercentValue);
        Assert.Equal("chrome.exe", vm.TopRamProcesses[0].Name);
    }

    [Fact]
    public void DashboardViewModel_SectionedStatistics_DisplaysCpuAndGpuMetricsWithThresholds()
    {
        var vm = new DashboardViewModel();

        var system = new SystemMetricsSnapshot(
            CpuPercent: 78.5,
            TotalMemoryBytes: 32L * 1024 * 1024 * 1024,
            UsedMemoryBytes: 8L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 24L * 1024 * 1024 * 1024,
            MemoryPercent: 25.0,
            ProcessIoBytesPerSec: 1024 * 1024,
            NetworkReceivedBytesPerSec: 50000,
            NetworkSentBytesPerSec: 25000,
            ProcessorName: "AMD Ryzen 7 5800H",
            LogicalCoreCount: 16,
            OsVersion: "Windows 11",
            ProcessCount: 150,
            Uptime: TimeSpan.FromHours(5),
            CpuClockMhz: 3850.0,
            CpuPowerWatts: 42.5,
            CpuTemperatureC: 78.0);

        vm.ApplyMetrics(Array.Empty<ProcessSnapshot>(), system);

        // CPU Section Assertions
        Assert.Equal("3,850 MHz", vm.CpuClockDisplay);
        Assert.Equal("42.5 W", vm.CpuPowerDisplay);
        Assert.Equal("78.0 °C", vm.CpuTemperatureDisplay);
        Assert.Equal("#F59E0B", vm.CpuTemperatureColor); // Amber at 78C
        Assert.Equal("78.5%", vm.CpuUtilizationDisplay);

        // GPU Section Assertions
        Assert.Contains("MHz", vm.GpuClockDisplay);
        Assert.Contains("W", vm.GpuPowerDisplay);
        Assert.Contains("°C", vm.GpuTemperatureDisplay);
        Assert.Contains("V", vm.GpuVoltageDisplay);
        Assert.Contains("MHz", vm.VramClockDisplay);
        Assert.Contains("%", vm.GpuUtilizationDisplay);
    }
}
