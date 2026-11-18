using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeuralTask.App.Services;

namespace NeuralTask.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { File.WriteAllText("crash.log", args.ExceptionObject?.ToString() ?? "Unknown exception"); } catch { }
        };
        DispatcherUnhandledException += (s, args) =>
        {
            try { File.WriteAllText("crash.log", args.Exception?.ToString() ?? "Unknown dispatcher exception"); } catch { }
        };

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 1. Initialize DI Container
        AppHost.InitializeServices();

        var settingsService = AppHost.Services.GetRequiredService<ISettingsService>();
        var historyStore = AppHost.Services.GetRequiredService<NeuralTask.Core.Services.IHistoryStore>();
        if (settingsService.CurrentSettings.IsHistoryPersistenceEnabled)
        {
            _ = historyStore.LoadPersistedAsync();
        }

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        this.MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();

        // 2. Automated Screenshot Capture Mode (if requested via CLI)
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        if (screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length)
        {
            var outputDir = e.Args[screenshotIndex + 1];
            _ = RunScreenshotCaptureAsync(mainWindow, outputDir);
        }
    }

    private async Task RunScreenshotCaptureAsync(MainWindow mainWindow, string outputDir)
    {
        // Wait for window initialization and initial monitoring ticks
        await Task.Delay(800);

        try
        {
            await mainWindow.CaptureScreenshotsAsync(outputDir);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "screenshot_error.txt"), ex.ToString());
        }

        Shutdown(0);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Ensure clean shutdown of coordinator and background monitoring loop
        try
        {
            var historyStore = AppHost.Services.GetService<NeuralTask.Core.Services.IHistoryStore>();
            if (historyStore != null && historyStore.IsPersistenceEnabled)
            {
                await historyStore.PersistAsync();
            }

            var coordinator = AppHost.Services.GetService<ProcessMonitorCoordinator>();
            if (coordinator != null)
            {
                await coordinator.StopAsync();
                coordinator.Dispose();
            }
        }
        catch
        {
        }

        base.OnExit(e);
    }
}
