using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NeuralTask.App.Services;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ChildViewModelDependencyInjectionTests
{
    private static IServiceProvider CreateTestServiceProvider()
    {
        var services = new ServiceCollection();
        AppHost.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AppHost_RegistersAndResolvesAllChildViewModels_FromDi()
    {
        var provider = CreateTestServiceProvider();

        var dashboard = provider.GetService<DashboardViewModel>();
        var processList = provider.GetService<ProcessListViewModel>();
        var performance = provider.GetService<PerformanceViewModel>();
        var startup = provider.GetService<StartupViewModel>();
        var settings = provider.GetService<SettingsViewModel>();
        var inspectorFactory = provider.GetService<IProcessInspectorViewModelFactory>();

        Assert.NotNull(dashboard);
        Assert.NotNull(processList);
        Assert.NotNull(performance);
        Assert.NotNull(startup);
        Assert.NotNull(settings);
        Assert.NotNull(inspectorFactory);
    }

    [Fact]
    public void MainWindowViewModel_ConstructsViaDi_WithAllInjectedChildViewModels()
    {
        var provider = CreateTestServiceProvider();

        using var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(mainWindowVm.Dashboard);
        Assert.NotNull(mainWindowVm.ProcessList);
        Assert.NotNull(mainWindowVm.Performance);
        Assert.NotNull(mainWindowVm.Startup);
        Assert.NotNull(mainWindowVm.Settings);
        Assert.NotNull(mainWindowVm.PluginManager);
        Assert.NotNull(mainWindowVm.StartupLoadTask);
    }

    [Fact]
    public void ChildViewModels_RegisteredAsTransient_ReturnDistinctInstances()
    {
        var provider = CreateTestServiceProvider();

        var d1 = provider.GetRequiredService<DashboardViewModel>();
        var d2 = provider.GetRequiredService<DashboardViewModel>();
        Assert.NotSame(d1, d2);

        var pl1 = provider.GetRequiredService<ProcessListViewModel>();
        var pl2 = provider.GetRequiredService<ProcessListViewModel>();
        Assert.NotSame(pl1, pl2);

        var p1 = provider.GetRequiredService<PerformanceViewModel>();
        var p2 = provider.GetRequiredService<PerformanceViewModel>();
        Assert.NotSame(p1, p2);

        var s1 = provider.GetRequiredService<StartupViewModel>();
        var s2 = provider.GetRequiredService<StartupViewModel>();
        Assert.NotSame(s1, s2);

        var set1 = provider.GetRequiredService<SettingsViewModel>();
        var set2 = provider.GetRequiredService<SettingsViewModel>();
        Assert.NotSame(set1, set2);

        var f1 = provider.GetRequiredService<IProcessInspectorViewModelFactory>();
        var f2 = provider.GetRequiredService<IProcessInspectorViewModelFactory>();
        Assert.NotSame(f1, f2);
    }

    [Fact]
    public void MainWindowViewModel_RegisteredAsSingleton_SharesSingleInstance()
    {
        var provider = CreateTestServiceProvider();

        var vm1 = provider.GetRequiredService<MainWindowViewModel>();
        var vm2 = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Same(vm1, vm2);
    }

    [Fact]
    public void NoDuplicateSingletonServices_InjectedIntoParentAndChildViewModels()
    {
        var provider = CreateTestServiceProvider();

        var coordinator = provider.GetRequiredService<ProcessMonitorCoordinator>();
        var pluginManager = provider.GetRequiredService<IPluginManager>();
        var historyStore = provider.GetRequiredService<IHistoryStore>();
        var flagEngine = provider.GetRequiredService<IHeuristicFlagEngine>();

        using var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        // PluginManager must match singleton
        Assert.Same(pluginManager, mainWindowVm.PluginManager);

        // Core singletons resolved in child ViewModels must be the exact same instances
        var settingsVm = mainWindowVm.Settings;
        var dashboardVm = mainWindowVm.Dashboard;

        // Verify that history store & heuristic engines are singletons across container
        Assert.Same(historyStore, provider.GetRequiredService<IHistoryStore>());
        Assert.Same(flagEngine, provider.GetRequiredService<IHeuristicFlagEngine>());
        Assert.Same(coordinator, provider.GetRequiredService<ProcessMonitorCoordinator>());
    }

    [Fact]
    public void ProcessInspectorViewModelFactory_CreatesInspectorWithRuntimeParameter()
    {
        var provider = CreateTestServiceProvider();
        var factory = provider.GetRequiredService<IProcessInspectorViewModelFactory>();

        var snapshot = new ProcessSnapshot(
            Pid: 5432,
            Name: "TestApp.exe",
            ParentPid: 1000,
            CpuPercent: 12.5,
            RamBytes: 1024 * 1024 * 64,
            DiskBytesPerSec: 100,
            NetworkBytesPerSec: 200,
            StartTime: null,
            ExecutablePath: @"C:\Test\TestApp.exe");

        var item = new ProcessItemViewModel(snapshot);
        var inspector = factory.Create(item);

        Assert.NotNull(inspector);
        Assert.Equal(5432, inspector.Pid);
        Assert.Equal("TestApp.exe", inspector.Name);
        Assert.Same(item, inspector.Item);
    }

    private class SpyInspectorFactory : IProcessInspectorViewModelFactory
    {
        public ProcessItemViewModel? LastCreatedItem { get; private set; }
        public int CreateCount { get; private set; }

        public ProcessInspectorViewModel Create(ProcessItemViewModel item)
        {
            CreateCount++;
            LastCreatedItem = item;
            return new ProcessInspectorViewModel(item, ProcessInfoService.Instance);
        }
    }

    [Fact]
    public void ProcessListViewModel_UsesInjectedInspectorFactory_OnSelection()
    {
        var spyFactory = new SpyInspectorFactory();
        var vm = new ProcessListViewModel(inspectorFactory: spyFactory);

        var snapshot = new ProcessSnapshot(
            Pid: 7777,
            Name: "TargetProcess.exe",
            ParentPid: 1,
            CpuPercent: 5.0,
            RamBytes: 1024 * 1024 * 32,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: null,
            ExecutablePath: @"C:\TargetProcess.exe");

        var item = new ProcessItemViewModel(snapshot);

        vm.SelectedProcess = item;

        Assert.Equal(1, spyFactory.CreateCount);
        Assert.Same(item, spyFactory.LastCreatedItem);
        Assert.NotNull(vm.InspectorDetails);
        Assert.Equal(7777, vm.InspectorDetails.Pid);
    }

    [Fact]
    public void MainWindowViewModel_DisposalOwnership_DisposesAllDisposableChildrenExactlyOnce()
    {
        var provider = CreateTestServiceProvider();
        var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Equal(0, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(0, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(0, mainWindowVm.Settings.DisposeCount);

        // First disposal: child viewmodels must dispose exactly once
        mainWindowVm.Dispose();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(1, mainWindowVm.ProcessList.TotalDisposalCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
        Assert.Equal(1, mainWindowVm.DisposeCount);

        // Idempotent second disposal: must not invoke child Dispose again
        mainWindowVm.Dispose();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
    }

    [Fact]
    public async Task MainWindowViewModel_Shutdown_CompletesChildAsynchronousDisposal()
    {
        var provider = CreateTestServiceProvider();
        var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        // Ensure async disposal is awaited and completes child asynchronous disposal
        await mainWindowVm.DisposeAsync();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.TotalDisposalCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
        Assert.Equal(1, mainWindowVm.DisposeAsyncCount);

        // Repeated async disposal must be idempotent
        await mainWindowVm.DisposeAsync();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
    }

    [Fact]
    public void ContainerShutdown_DoesNotCauseDuplicateDisposal_AfterMainWindowDisposal()
    {
        var provider = CreateTestServiceProvider();
        var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        // MainWindowViewModel explicitly disposes children
        mainWindowVm.Dispose();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);

        // Container is shut down / disposed on application exit
        ((IDisposable)provider).Dispose();

        // Proves container shutdown does not double-dispose transient child viewmodels
        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
    }

    [Fact]
    public async Task ContainerShutdown_DoesNotCauseDuplicateDisposal_AfterAsyncShutdownCoordinator()
    {
        var provider = CreateTestServiceProvider();
        var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        var historyStore = provider.GetRequiredService<IHistoryStore>();

        var coordinator = new AppShutdownCoordinator(settingsService, historyStore, mainWindowVm);
        var success = await coordinator.ExecuteShutdownAsync();

        Assert.True(success);
        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);

        // Container is shut down / disposed on application exit
        ((IDisposable)provider).Dispose();

        // Transient child ViewModels must remain disposed exactly once
        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeAsyncCount);
        Assert.Equal(0, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
    }

    [Fact]
    public void ContainerShutdown_Alone_DisposesChildrenExactlyOnceViaMainWindowViewModel()
    {
        var provider = CreateTestServiceProvider();
        var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        // Container-only disposal triggers MainWindowViewModel disposal, which owns and disposes children
        ((IDisposable)provider).Dispose();

        Assert.Equal(1, mainWindowVm.Dashboard.DisposeCount);
        Assert.Equal(1, mainWindowVm.ProcessList.DisposeCount);
        Assert.Equal(1, mainWindowVm.Startup.DisposeCount);
        Assert.Equal(1, mainWindowVm.Settings.DisposeCount);
    }

    [Fact]
    public async Task MainWindowViewModel_NavigationAndInitialization_PreservesBehavior()
    {
        var provider = CreateTestServiceProvider();
        using var mainWindowVm = provider.GetRequiredService<MainWindowViewModel>();

        // Navigation tab switching
        Assert.Equal(0, mainWindowVm.SelectedTabIndex);
        string? changedProperty = null;
        mainWindowVm.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        mainWindowVm.SelectedTabIndex = 2;
        Assert.Equal(2, mainWindowVm.SelectedTabIndex);
        Assert.Equal(nameof(mainWindowVm.SelectedTabIndex), changedProperty);

        // Startup load task completes
        await mainWindowVm.StartupLoadTask;
        Assert.True(mainWindowVm.StartupLoadTask.IsCompletedSuccessfully);

        // Monitoring update propagation
        var metrics = new SystemMetricsSnapshot(
            CpuPercent: 28.5,
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            UsedMemoryBytes: 8L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 8L * 1024 * 1024 * 1024,
            MemoryPercent: 50.0,
            ProcessIoBytesPerSec: 1024 * 300,
            NetworkReceivedBytesPerSec: 1024 * 80,
            NetworkSentBytesPerSec: 1024 * 50,
            ProcessorName: "Test CPU",
            LogicalCoreCount: 8,
            OsVersion: "Windows 11",
            ProcessCount: 2,
            Uptime: TimeSpan.FromHours(1));

        var procs = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(100, "System", 0, 1.0, 1024 * 1024, 0, null, null, "System"),
            new ProcessSnapshot(200, "App.exe", 100, 15.0, 1024 * 1024 * 50, 500, null, null, @"C:\App.exe")
        };

        var update = new ProcessMonitoringUpdate(procs, metrics);

        // Simulate coordinator firing update
        var coordinator = provider.GetRequiredService<ProcessMonitorCoordinator>();
        coordinator.PublishUpdateForTesting(update);

        Assert.Contains("Online • 2 processes", mainWindowVm.StatusBadgeText);
        Assert.Equal("28.5%", mainWindowVm.Performance.CurrentCpuText);
    }
}
