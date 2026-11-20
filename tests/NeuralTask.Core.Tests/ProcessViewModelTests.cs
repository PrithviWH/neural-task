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
    public void ProcessListViewModel_StableSortingLocksOrderOnMicroFluctuations()
    {
        var vm = new ProcessListViewModel();
        // Initial snapshot with two idle processes
        var t1 = new List<ProcessSnapshot>
        {
            new(100, "procA.exe", 0, 0.01, 1000, 0, 0, null, null, false),
            new(200, "procB.exe", 0, 0.02, 1000, 0, 0, null, null, false)
        };
        vm.ApplySnapshot(t1);

        int initialFirstPid = vm.FilteredProcesses[0].Pid;
        int initialSecondPid = vm.FilteredProcesses[1].Pid;

        // Next tick: micro-fluctuation where CPU shifts by 0.01% (below 0.25% noise deadband)
        var t2 = new List<ProcessSnapshot>
        {
            new(100, "procA.exe", 0, 0.02, 1000, 0, 0, null, null, false),
            new(200, "procB.exe", 0, 0.01, 1000, 0, 0, null, null, false)
        };
        vm.ApplySnapshot(t2);

        // Order must remain stable despite micro-fluctuation
        Assert.Equal(initialFirstPid, vm.FilteredProcesses[0].Pid);
        Assert.Equal(initialSecondPid, vm.FilteredProcesses[1].Pid);

        // Genuine shift (> 0.25%) reorders
        var t3 = new List<ProcessSnapshot>
        {
            new(100, "procA.exe", 0, 5.0, 1000, 0, 0, null, null, false),
            new(200, "procB.exe", 0, 0.01, 1000, 0, 0, null, null, false)
        };
        vm.ApplySnapshot(t3);
        Assert.Equal(100, vm.FilteredProcesses[0].Pid);
        Assert.Equal(200, vm.FilteredProcesses[1].Pid);
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

    [Fact]
    public void ProcessItemViewModel_SystemProcessAndActionSafetyIdentified()
    {
        // 1. Critical System Process (PID 4)
        var kernelSnap = new ProcessSnapshot(4, "System", 0, 0.5, 1024 * 1024 * 10, 0, 0, null, null, true);
        var kernelVm = new ProcessItemViewModel(kernelSnap);
        Assert.True(kernelVm.IsSystemProcess);
        Assert.True(kernelVm.IsCriticalSystemProcess);
        Assert.False(kernelVm.CanTerminate);

        // 2. Windows Service in System32
        var svcSnap = new ProcessSnapshot(1234, "svchost.exe", 4, 1.2, 1024 * 1024 * 30, 0, 0, null, @"C:\Windows\System32\svchost.exe", false);
        var svcVm = new ProcessItemViewModel(svcSnap);
        Assert.True(svcVm.IsSystemProcess);
        Assert.False(svcVm.IsCriticalSystemProcess);
        Assert.True(svcVm.CanTerminate);

        // 3. User application
        var userSnap = new ProcessSnapshot(5678, "Code.exe", 100, 18.5, 1024L * 1024L * 1500L, 0, 0, null, @"C:\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe", false);
        var userVm = new ProcessItemViewModel(userSnap);
        Assert.False(userVm.IsSystemProcess);
        Assert.False(userVm.IsCriticalSystemProcess);
        Assert.True(userVm.CanTerminate);
        Assert.True(userVm.IsHighCpu);
        Assert.True(userVm.IsHighRam);
        Assert.Equal(18.5, userVm.CpuBarPercent);
    }

    [Fact]
    public void ProcessListViewModel_ActionSafetyEnforcesSelectionAndCriticalProtection()
    {
        var vm = new ProcessListViewModel();
        var data = new List<ProcessSnapshot>
        {
            new(4, "System", 0, 0.5, 1024 * 1024 * 10, 0, 0, null, null, true),
            new(200, "user_app.exe", 1, 2.0, 1024 * 1024 * 50, 0, 0, null, null, false)
        };
        vm.ApplySnapshot(data);

        // Initially nothing selected
        Assert.Null(vm.SelectedProcess);
        Assert.False(vm.HasSelectedProcess);
        Assert.False(vm.CanTerminateSelected);
        Assert.False(vm.CanTerminateTreeSelected);

        // Select critical system process
        vm.SelectedProcess = vm.FilteredProcesses.First(p => p.Pid == 4);
        Assert.True(vm.HasSelectedProcess);
        Assert.False(vm.CanTerminateSelected);
        Assert.False(vm.CanTerminateTreeSelected);

        // Select user app
        vm.SelectedProcess = vm.FilteredProcesses.First(p => p.Pid == 200);
        Assert.True(vm.HasSelectedProcess);
        Assert.True(vm.CanTerminateSelected);
        Assert.True(vm.CanTerminateTreeSelected);
    }

    [Fact]
    public void ProcessListViewModel_CategoryFilteringWorksAccurately()
    {
        var vm = new ProcessListViewModel();
        var data = new List<ProcessSnapshot>
        {
            new(4, "System", 0, 0.5, 1024 * 1024 * 10, 0, 0, null, null, true),
            new(100, "notepad.exe", 1, 1.0, 1024 * 1024 * 20, 0, 0, null, @"C:\Tools\notepad.exe", false),
            new(200, "heavy_app.exe", 1, 25.0, 1024L * 1024L * 800L, 0, 0, null, @"C:\Tools\heavy.exe", false)
        };
        vm.ApplySnapshot(data);

        // 1. All
        vm.SelectedFilterCategory = "All";
        Assert.Equal(3, vm.FilteredProcesses.Count);

        // 2. Apps
        vm.SelectedFilterCategory = "Apps";
        Assert.Equal(2, vm.FilteredProcesses.Count);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 4);

        // 3. System
        vm.SelectedFilterCategory = "System";
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal(4, vm.FilteredProcesses[0].Pid);

        // 4. High Usage (CPU >= 5% or RAM >= 500MB)
        vm.SelectedFilterCategory = "HighUsage";
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal(200, vm.FilteredProcesses[0].Pid);
    }

    [Fact]
    public void ProcessListViewModel_TreeViewModeFlattensHierarchyAndCollapses()
    {
        var vm = new ProcessListViewModel();
        var parentSnap = new ProcessSnapshot(10, "parent.exe", 0, 1.0, 1000, 0, 0, null, null, false);
        var child1Snap = new ProcessSnapshot(11, "child1.exe", 10, 2.0, 2000, 0, 0, null, null, false);
        var child2Snap = new ProcessSnapshot(12, "child2.exe", 10, 3.0, 3000, 0, 0, null, null, false);

        vm.ApplySnapshot(new[] { parentSnap, child1Snap, child2Snap });

        // Switch to Tree View
        vm.IsTreeViewMode = true;
        Assert.True(vm.IsTreeViewMode);

        var parentItem = vm.FilteredProcesses.First(p => p.Pid == 10);
        Assert.True(parentItem.HasChildren);
        Assert.Equal(2, parentItem.ChildrenCount);
        Assert.Equal(0, parentItem.Depth);

        // With parent expanded (default), all 3 are visible
        Assert.Equal(3, vm.FilteredProcesses.Count);

        // Collapse parent
        parentItem.IsExpanded = false;
        // Only parent is visible
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal(10, vm.FilteredProcesses[0].Pid);

        // Re-expand parent
        parentItem.IsExpanded = true;
        Assert.Equal(3, vm.FilteredProcesses.Count);
    }

    [Fact]
    public void ProcessInspectorViewModel_SanitizesSensitiveCommandLineArguments()
    {
        string raw = @"C:\App\service.exe --port 8080 --token=super_secret_token_12345 --env=prod";
        string sanitized = ProcessInspectorViewModel.SanitizeCommandLine(raw);
        Assert.DoesNotContain("super_secret_token_12345", sanitized);
        Assert.Contains("--token=********", sanitized);
        Assert.Contains("--port 8080", sanitized);
    }

    [Fact]
    public async Task ProcessListViewModel_TerminateSelected_PromptsConfirmationCallback()
    {
        var vm = new ProcessListViewModel();
        var data = new List<ProcessSnapshot>
        {
            new(300, "app_to_kill.exe", 1, 1.0, 1024 * 1024 * 10, 0, 0, null, @"C:\Tools\app.exe", false)
        };
        vm.ApplySnapshot(data);
        vm.SelectedProcess = vm.FilteredProcesses.First(p => p.Pid == 300);

        bool promptInvoked = false;
        vm.ConfirmKillProcessCallback = target =>
        {
            promptInvoked = true;
            Assert.Equal(300, target.Pid);
            Assert.Equal("app_to_kill.exe", target.Name);
            return Task.FromResult(false); // Simulated user cancel
        };

        await vm.TerminateSelectedAsync();
        Assert.True(promptInvoked);
    }

    private class SuspendedProcessActionService : IProcessActionService
    {
        public TaskCompletionSource<bool> TerminationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SuspensionBlocker { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TerminateCalled { get; private set; }

        public ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots) => ProcessTreeBuilder.BuildTree(snapshots);

        public async Task<ProcessTerminationResult> TerminateProcessAsync(
            int pid,
            string? expectedProcessName = null,
            DateTimeOffset? expectedStartTime = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            TerminateCalled = true;
            TerminationStarted.TrySetResult(true);

            // 2. Underlying termination service is suspended
            await SuspensionBlocker.Task;

            return new ProcessTerminationResult(pid, expectedProcessName ?? "test.exe", ProcessTerminationStatus.Success, "Done");
        }

        public Task<TreeTerminationResult> TerminateTreeAsync(int rootPid, IReadOnlyList<ProcessSnapshot> currentSnapshots, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TreeTerminationResult(rootPid, Array.Empty<ProcessTerminationResult>(), true));
        }
    }

    [Fact]
    public async Task ProcessListViewModel_SuspendedTermination_DisposeCoordinatesAndReleasesWithoutException()
    {
        // 1. Termination begins
        var actionService = new SuspendedProcessActionService();
        var vm = new ProcessListViewModel(actionService: actionService);

        var snap = new ProcessSnapshot(7000, "suspended.exe", 1, 1.0, 1024, 0, 0, DateTimeOffset.UtcNow, null, false);
        vm.ApplySnapshot(new[] { snap });
        vm.SelectedProcess = vm.FilteredProcesses.First();

        var terminationTask = vm.TerminateSelectedAsync();

        // Wait until underlying termination service has started and is suspended
        await actionService.TerminationStarted.Task;
        Assert.True(actionService.TerminateCalled);

        // 3. Dispose is called
        var disposeEx = Record.Exception(() => vm.Dispose());
        Assert.Null(disposeEx);

        // 4. The operation is released
        actionService.SuspensionBlocker.TrySetResult(true);

        // 5. No exception occurs
        var taskEx = await Record.ExceptionAsync(async () => await terminationTask);
        Assert.Null(taskEx);

        // 6. All owned resources reach a valid final state
        Assert.Empty(vm.FilteredProcesses);
    }
}
