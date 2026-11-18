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

    public static void InitializeServices()
    {
        var services = new ServiceCollection();

        // 1. Core Services (Singletons)
        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
        services.AddSingleton<IProcessActionService, ProcessActionService>();

        // 2. Application Services
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ProcessMonitorCoordinator>();
        services.AddSingleton<TrayIconService>();

        // 3. ViewModels
        services.AddSingleton<MainWindowViewModel>();

        // 4. Windows
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }
}
