using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.App.Services;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessViewModelTests
{
    private class FakeSampler : IProcessDataSampler
    {
        public List<RawProcessSample> Samples { get; set; } = new();
        public IReadOnlyList<RawProcessSample> CollectRawSamples() => Samples;
    }

    private class FakeSystemMetricsService : ISystemMetricsService
    {
        public SystemMetricsSnapshot SnapshotToReturn { get; set; } = new(
            CpuPercent: 12.5,
            TotalMemoryBytes: 32_000_000_000,
            UsedMemoryBytes: 16_000_000_000,
            AvailableMemoryBytes: 16_000_000_000,
            MemoryPercent: 50.0,
            ProcessIoBytesPerSec: 10_000_000,
            NetworkReceivedBytesPerSec: 500_000,
            NetworkSentBytesPerSec: 100_000,
            ProcessorName: "Mock Test Processor",
            LogicalCoreCount: 8,
            OsVersion: "Windows 11 Test",
            ProcessCount: 150,
            Uptime: TimeSpan.FromHours(5)
        );

        public SystemMetricsSnapshot CollectSnapshot(IReadOnlyList<ProcessSnapshot> processes) => SnapshotToReturn;
    }

    [Fact]
    public void SnapshotToViewModelMapping_FormatsMetricsAndHandlesUnavailableValues()
    {
        // 1. Normal active process
        var normalSnapshot = new ProcessSnapshot(
            Pid: 100,
            Name: "worker.exe",
            ParentPid: 10,
            CpuPercent: 5.25,
            RamBytes: 1024 * 1024 * 250, // 250 MB
            DiskBytesPerSec: 1024 * 512,  // 512 KB/s
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.UtcNow,
            ExecutablePath: @"C:\Tools\worker.exe",
            IsAccessDenied: false
        );

        var normalVm = new ProcessItemViewModel(normalSnapshot);
        Assert.Equal(100, normalVm.Pid);
        Assert.Equal("worker.exe", normalVm.Name);
        Assert.Equal("5.25%", normalVm.CpuDisplay);
        Assert.Equal("250.0 MB", normalVm.RamDisplay);
        Assert.Equal("512.0 KB/s", normalVm.IoDisplay);
        Assert.Equal("—", normalVm.NetworkDisplay); // Network is strictly — per prompt rules
        Assert.False(normalVm.IsAccessDenied);

        // 2. Protected/Access-denied process
        var protectedSnapshot = new ProcessSnapshot(
            Pid: 4,
            Name: "System",
            ParentPid: 0,
            CpuPercent: 0,
            RamBytes: 1024 * 1024 * 16,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: null,
            ExecutablePath: null,
            IsAccessDenied: true
        );

        var protectedVm = new ProcessItemViewModel(protectedSnapshot);
        Assert.True(protectedVm.IsAccessDenied);
        Assert.Equal("—", protectedVm.CpuDisplay);
        Assert.Equal("—", protectedVm.IoDisplay);
        Assert.Equal("—", protectedVm.NetworkDisplay);
    }

    [Fact]
    public void ProcessListViewModel_HandlesProcessAdditionRemovalAndIdentity()
    {
        var vm = new ProcessListViewModel();

        // Tick 1: Two processes
        var tick1 = new List<ProcessSnapshot>
        {
            new(101, "procA.exe", 1, 1.0, 1000, 100, 0, null, null, false),
            new(102, "procB.exe", 1, 2.0, 2000, 200, 0, null, null, false)
        };

        vm.ApplySnapshot(tick1);
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.FilteredProcesses.Count);
        var item101 = vm.FilteredProcesses.First(p => p.Pid == 101);

        // Tick 2: procA updated, procB exited, procC added, procA_duplicate (same name, distinct PID)
        var tick2 = new List<ProcessSnapshot>
        {
            new(101, "procA.exe", 1, 3.5, 1500, 300, 0, null, null, false),
            new(103, "procC.exe", 1, 0.5, 500, 50, 0, null, null, false),
            new(104, "procA.exe", 1, 0.8, 800, 80, 0, null, null, false)
        };

        vm.ApplySnapshot(tick2);
        Assert.Equal(3, vm.TotalCount);

        // In-place identity preserved for PID 101
        var item101Updated = vm.FilteredProcesses.First(p => p.Pid == 101);
        Assert.Same(item101, item101Updated);
        Assert.Equal("3.50%", item101Updated.CpuDisplay);

        // PID 102 removed
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 102);

        // PID 104 added despite duplicate name
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 104);
    }

    [Fact]
    public void ProcessListViewModel_SortingAndFuzzySearch()
    {
        var vm = new ProcessListViewModel();
        var data = new List<ProcessSnapshot>
        {
            new(10, "Antigravity.exe", 1, 4.0, 4000, 0, 0, null, null, false),
            new(20, "chrome.exe", 1, 8.0, 8000, 0, 0, null, null, false),
            new(30, "code.exe", 1, 1.0, 2000, 0, 0, null, null, false),
            new(40, "explorer.exe", 1, 2.0, 6000, 0, 0, null, null, false)
        };

        vm.ApplySnapshot(data);

        // 1. Sort by CPU descending (default)
        Assert.Equal(20, vm.FilteredProcesses[0].Pid); // 8.0%
        Assert.Equal(10, vm.FilteredProcesses[1].Pid); // 4.0%

        // 2. Sort by Name ascending
        vm.SortBy("Name");
        Assert.Equal("Antigravity.exe", vm.FilteredProcesses[0].Name);

        // 3. Fuzzy Search: substring
        vm.SearchText = "chrom";
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("chrome.exe", vm.FilteredProcesses[0].Name);

        // 4. Fuzzy Search: PID match
        vm.SearchText = "40";
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal(40, vm.FilteredProcesses[0].Pid);

        // 5. Fuzzy Search: subsequence match ("cde" matches "code.exe")
        vm.SearchText = "cde";
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("code.exe", vm.FilteredProcesses[0].Name);

        // Clear filter
        vm.SearchText = "";
        Assert.Equal(4, vm.FilteredProcesses.Count);
    }

    [Fact]
    public void DashboardViewModel_SelectsTopCpuAndMemoryProcesses()
    {
        var vm = new DashboardViewModel();
        var sys = new SystemMetricsSnapshot(
            CpuPercent: 25.0,
            TotalMemoryBytes: 32_000_000_000,
            UsedMemoryBytes: 16_000_000_000,
            AvailableMemoryBytes: 16_000_000_000,
            MemoryPercent: 50.0,
            ProcessIoBytesPerSec: 5_000_000,
            NetworkReceivedBytesPerSec: 100_000,
            NetworkSentBytesPerSec: 50_000,
            ProcessorName: "Test Core",
            LogicalCoreCount: 8,
            OsVersion: "Windows 11",
            ProcessCount: 4,
            Uptime: TimeSpan.FromHours(2)
        );

        var processes = new List<ProcessSnapshot>
        {
            new(1, "low.exe", 0, 0.5, 100 * 1024 * 1024, 0, 0, null, null, false),
            new(2, "cpu_heavy.exe", 0, 15.0, 200 * 1024 * 1024, 0, 0, null, null, false),
            new(3, "ram_heavy.exe", 0, 2.0, 900 * 1024 * 1024, 0, 0, null, null, false),
            new(4, "mid.exe", 0, 7.5, 500 * 1024 * 1024, 0, 0, null, null, false)
        };

        vm.ApplyMetrics(processes, sys);

        // Top CPU: cpu_heavy (15%), mid (7.5%), ram_heavy (2.0%)
        Assert.Equal("cpu_heavy.exe", vm.TopCpuProcesses[0].Name);
        Assert.Equal("15.00%", vm.TopCpuProcesses[0].ValueDisplay);
        Assert.Equal("mid.exe", vm.TopCpuProcesses[1].Name);

        // Top RAM: ram_heavy (900 MB), mid (500 MB), cpu_heavy (200 MB)
        Assert.Equal("ram_heavy.exe", vm.TopRamProcesses[0].Name);
        Assert.Equal("mid.exe", vm.TopRamProcesses[1].Name);

        // Metrics formatted
        Assert.Equal("25.0%", vm.CpuDisplay);
        Assert.Equal("50.0% of 29.8 GB", vm.RamDetailDisplay);
    }

    [Fact]
    public void PerformanceHistoryBuffer_KeepsHistoryBoundedWithoutMemoryLeak()
    {
        var buffer = new PerformanceHistoryBuffer(maxSamples: 60);

        // Add 150 samples
        for (int i = 1; i <= 150; i++)
        {
            buffer.AddSample(i);
        }

        // Bounded capacity enforced
        Assert.Equal(60, buffer.Count);

        var snapshot = buffer.GetSnapshot();
        Assert.Equal(60, snapshot.Count);
        // Oldest retained sample must be 150 - 60 + 1 = 91
        Assert.Equal(91.0, snapshot[0]);
        // Newest retained sample must be 150
        Assert.Equal(150.0, snapshot[59]);

        // PointCollection generation
        var points = buffer.GeneratePoints(800, 150, 150.0);
        Assert.Equal(60, points.Count);
        Assert.Equal(0.0, points[0].X);
        Assert.Equal(800.0, points[59].X);
    }

    [Fact]
    public void SettingsViewModel_SafelyUpdatesRefreshIntervalAndPersists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new JsonSettingsService(tempFile);
            var fakeSampler = new FakeSampler();
            var monitorService = new ProcessMonitorService(fakeSampler);
            var fakeMetrics = new FakeSystemMetricsService();
            using var coordinator = new ProcessMonitorCoordinator(monitorService, fakeMetrics);

            var vm = new SettingsViewModel(settingsService, coordinator);

            // Change interval to 500ms
            vm.SelectedIntervalMs = 500;
            Assert.True(vm.IsInterval500);
            Assert.Equal(TimeSpan.FromMilliseconds(500), coordinator.RefreshInterval);

            // Change interval to 2000ms
            vm.SelectedIntervalMs = 2000;
            Assert.True(vm.IsInterval2000);
            Assert.Equal(TimeSpan.FromMilliseconds(2000), coordinator.RefreshInterval);

            // Verify persisted in settings file
            var reloaded = new JsonSettingsService(tempFile);
            Assert.Equal(2000, reloaded.CurrentSettings.RefreshIntervalMs);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessMonitorCoordinator_DisposalStopsMonitoringCleanly()
    {
        var fakeSampler = new FakeSampler();
        var monitorService = new ProcessMonitorService(fakeSampler);
        var fakeMetrics = new FakeSystemMetricsService();

        var coordinator = new ProcessMonitorCoordinator(monitorService, fakeMetrics);
        await coordinator.StartAsync();
        Assert.True(coordinator.IsRunning);

        coordinator.Dispose();
        Assert.False(coordinator.IsRunning);
    }
}
