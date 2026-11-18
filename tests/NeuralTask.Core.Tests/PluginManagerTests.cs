using System;
using System.IO;
using System.Linq;
using System.Threading;
using NeuralTask.App.Services;
using NeuralTask.Contracts;
using Xunit;

namespace NeuralTask.Core.Tests;

public class PluginManagerTests
{
    private static void RunInSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }
    }
    [Fact]
    public void PluginManager_WithEmptyOrMissingDirectory_InitializesWithoutException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NeuralTask_Test_Plugins_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = new PluginManager(tempDir);
            manager.DiscoverAndLoadPlugins();

            Assert.True(Directory.Exists(tempDir));
            Assert.Empty(manager.LoadedPanels);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void PluginManager_WithCorruptedOrNonDotNetDll_SkipsGracefullyWithoutCrash()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NeuralTask_Test_BadDll_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write fake corrupted binary content
            File.WriteAllBytes(Path.Combine(tempDir, "corrupted_plugin.dll"), new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22 });

            using var manager = new PluginManager(tempDir);
            manager.DiscoverAndLoadPlugins();

            Assert.Empty(manager.LoadedPanels);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void PluginManager_WhenGpuPluginDllPresent_DiscoversAndLoadsGpuPanel()
    {
        RunInSta(() =>
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var gpuDllCandidate = Path.Combine(baseDir, "NeuralTask.Plugins.Gpu.dll");

            if (!File.Exists(gpuDllCandidate))
            {
                var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
                gpuDllCandidate = Path.Combine(solutionDir, "src", "plugins", "NeuralTask.Plugins.Gpu", "bin", "Debug", "net8.0-windows", "NeuralTask.Plugins.Gpu.dll");
            }

            Assert.True(File.Exists(gpuDllCandidate), $"GPU plugin DLL not found at: {gpuDllCandidate}");

            var tempPluginDir = Path.Combine(Path.GetTempPath(), "NeuralTask_Test_GpuPlugin_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPluginDir);

            try
            {
                File.Copy(gpuDllCandidate, Path.Combine(tempPluginDir, "NeuralTask.Plugins.Gpu.dll"), overwrite: true);

                using var manager = new PluginManager(tempPluginDir);

                bool panelLoadedFired = false;
                manager.PanelLoaded += p =>
                {
                    if (p.Id == "gpu-monitor") panelLoadedFired = true;
                };

                manager.DiscoverAndLoadPlugins();

                Assert.NotEmpty(manager.LoadedPanels);
                var gpuPanel = manager.LoadedPanels.FirstOrDefault(p => p.Id == "gpu-monitor");
                Assert.NotNull(gpuPanel);
                Assert.Equal("GPU Monitor", gpuPanel.Title);
                Assert.True(panelLoadedFired);
                Assert.False(gpuPanel.IsFaulted);

                // Verify telemetry dispatch
                var context = new PluginTelemetryContext(25.0, 16_000_000_000, 8_000_000_000, 8_000_000_000, 200, DateTime.UtcNow);
                manager.DispatchTelemetryTick(context);
                Assert.False(gpuPanel.IsFaulted);

                // Verify clean unload
                manager.UnloadAll();
                Assert.Empty(manager.LoadedPanels);
            }
            finally
            {
                if (Directory.Exists(tempPluginDir))
                {
                    try { Directory.Delete(tempPluginDir, recursive: true); } catch { }
                }
            }
        });
    }
}
