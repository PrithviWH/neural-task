using System;
using System.Windows;
using NeuralTask.Contracts;
using NeuralTask.Plugins.Gpu.Views;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// First-party example plugin for NeuralTask HUD.
/// Implements <see cref="INeuralTaskPanel"/> to provide live GPU 3D engine and VRAM monitoring.
/// </summary>
public class GpuPanel : INeuralTaskPanel
{
    private GpuTelemetrySampler? _sampler;
    private GpuPanelViewModel? _viewModel;
    private GpuPanelView? _view;

    public string Id => "gpu-monitor";
    public string Title => "GPU Monitor";
    public string Description => "Real-time DirectX / 3D Graphics Engine & Video Memory (VRAM) Telemetry";

    /// <summary>
    /// Vector geometry for a cybernetic GPU microchip.
    /// </summary>
    public string IconGeometry =>
        "M 4,4 L 20,4 L 20,20 L 4,20 Z M 7,7 L 17,7 L 17,17 L 7,17 Z M 2,9 L 4,9 M 2,15 L 4,15 M 20,9 L 22,9 M 20,15 L 22,15 M 9,2 L 9,4 M 15,2 L 15,4 M 9,20 L 9,22 M 15,20 L 15,22";

    public int Order => 10;

    public void Initialize()
    {
        _sampler = new GpuTelemetrySampler();
        _viewModel = new GpuPanelViewModel();

        // Perform initial baseline hardware sample asynchronously on a background thread
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var initialMetrics = _sampler.Sample();
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() => _viewModel?.ApplyMetrics(initialMetrics));
                }
            }
            catch
            {
                // Sampler handles missing counters gracefully
            }
        });
    }

    public FrameworkElement CreateView()
    {
        if (_viewModel == null)
        {
            Initialize();
        }

        _view = new GpuPanelView(_viewModel!);
        return _view;
    }

    public void OnTelemetryTick(PluginTelemetryContext? context)
    {
        if (_sampler == null || _viewModel == null) return;

        try
        {
            var metrics = _sampler.Sample();

            // Ensure ViewModel update is marshalled onto the UI thread
            if (Application.Current != null && Application.Current.Dispatcher != null &&
                !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(() => _viewModel.ApplyMetrics(metrics));
            }
            else
            {
                _viewModel.ApplyMetrics(metrics);
            }
        }
        catch
        {
        }
    }

    public void Shutdown()
    {
        _sampler?.Dispose();
        _sampler = null;
        _view = null;
        _viewModel = null;
    }

    public void Dispose()
    {
        Shutdown();
    }
}
