using System;
using Microsoft.Extensions.DependencyInjection;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Services;

namespace NeuralTask.App.Services;

public static class AppHost
{
    private static IServiceProvider? _serviceProvider;

    public static IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("AppHost services have not been initialized.");

    public static IServiceCollection ConfigureServices(IServiceCollection? services = null)
    {
        services ??= new ServiceCollection();

        // 1. Core Services (Singletons)
        services.AddSingleton<INetworkStatsProvider, DefaultNetworkStatsProvider>();
        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
        services.AddSingleton<IProcessActionService, ProcessActionService>();
        services.AddSingleton<IProcessInfoService, ProcessInfoService>();
        services.AddSingleton<IStartupScoreEngine, StartupScoreEngine>();
        services.AddSingleton<IHeuristicFlagEngine, HeuristicFlagEngine>();
        services.AddSingleton<IHistoryStore, HistoryStore>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IGpuMetricsService, GpuMetricsService>();
        services.AddSingleton<IStorageInfoService, StorageInfoService>();
        services.AddSingleton<INetworkInfoService, NetworkInfoService>();

        // 2. Application Services
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddSingleton<ProcessMonitorCoordinator>();
        services.AddSingleton<TrayIconService>();

        // 3. Child ViewModel Factories & ViewModels (Transient)
        services.AddTransient<IProcessInspectorViewModelFactory, ProcessInspectorViewModelFactory>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProcessListViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<SettingsViewModel>();

        // 4. Root ViewModels
        // MainWindowViewModel explicitly owns the lifecycle and disposal of its child ViewModels.
        // To prevent Microsoft DI from capturing transient IDisposable child ViewModels into the root container's
        // disposables list (which causes duplicate disposal upon container shutdown), child ViewModels are instantiated
        // for MainWindowViewModel via ActivatorUtilities, establishing explicit Composite Parent Ownership.
        services.AddSingleton<MainWindowViewModel>(sp =>
        {
            var coordinator = sp.GetRequiredService<ProcessMonitorCoordinator>();
            var trayIconService = sp.GetRequiredService<TrayIconService>();
            var pluginManager = sp.GetRequiredService<IPluginManager>();

            var dashboard = ActivatorUtilities.CreateInstance<DashboardViewModel>(sp);
            var processList = ActivatorUtilities.CreateInstance<ProcessListViewModel>(sp);
            var performance = ActivatorUtilities.CreateInstance<PerformanceViewModel>(sp);
            var startup = ActivatorUtilities.CreateInstance<StartupViewModel>(sp);
            var settings = ActivatorUtilities.CreateInstance<SettingsViewModel>(sp);

            return new MainWindowViewModel(
                coordinator,
                trayIconService,
                pluginManager,
                dashboard,
                processList,
                performance,
                startup,
                settings);
        });

        // 5. Windows
        services.AddTransient<MainWindow>();

        return services;
    }

    public static void InitializeServices()
    {
        var services = ConfigureServices();
        _serviceProvider = services.BuildServiceProvider();
    }

    public static void DisposeServices()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
            _serviceProvider = null;
        }
    }
}
