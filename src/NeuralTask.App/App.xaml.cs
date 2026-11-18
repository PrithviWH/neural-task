using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace NeuralTask.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();

        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        if (screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length)
        {
            var outputDir = e.Args[screenshotIndex + 1];
            mainWindow.Show();

            // Wait until window layout is initialized
            await Task.Delay(400);

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
        else
        {
            mainWindow.Show();
        }
    }
}
