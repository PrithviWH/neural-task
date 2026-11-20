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
    public static Task HistoryLoadTask { get; private set; } = Task.CompletedTask;

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
            // Prevent non-fatal dispatcher UI errors (such as unexpected view/binding exceptions) from terminating the app
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 1. Initialize DI Container
        AppHost.InitializeServices();

        var settingsService = AppHost.Services.GetRequiredService<ISettingsService>();
        var historyStore = AppHost.Services.GetRequiredService<NeuralTask.Core.Services.IHistoryStore>();
        if (settingsService.CurrentSettings.IsHistoryPersistenceEnabled)
        {
            HistoryLoadTask = Task.Run(async () =>
            {
                try
                {
                    await historyStore.LoadPersistedAsync();
                }
                catch
                {
                }
            });
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

        await mainWindow.ExecuteAsyncShutdownAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppHost.DisposeServices();
        }
        catch
        {
        }

        base.OnExit(e);
    }
}
