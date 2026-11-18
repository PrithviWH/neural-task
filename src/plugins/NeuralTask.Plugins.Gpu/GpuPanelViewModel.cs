using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// Presentation ViewModel for the GPU telemetry panel.
/// </summary>
public class GpuPanelViewModel : INotifyPropertyChanged
{
    private readonly LinkedList<double> _history = new();
    private const int MaxHistoryPoints = 40;

    private string _adapterName = "Detecting Graphics Hardware...";
    private double _utilizationPercent;
    private double _dedicatedMemoryUsedMb;
    private double _dedicatedMemoryTotalMb = 4096;
    private double _dedicatedMemoryPercent;
    private double _sharedMemoryUsedMb;
    private double _engine3DPercent;
    private double _engineVideoDecodePercent;
    private double _engineCopyPercent;
    private double _engineComputePercent;
    private string _sparklineGeometry = "M 0,60 L 300,60";
    private string _statusText = "D3D // HARDWARE ACCELERATION MONITOR";

    public string AdapterName
    {
        get => _adapterName;
        set => SetProperty(ref _adapterName, value);
    }

    public double UtilizationPercent
    {
        get => _utilizationPercent;
        set => SetProperty(ref _utilizationPercent, value);
    }

    public double DedicatedMemoryUsedMb
    {
        get => _dedicatedMemoryUsedMb;
        set => SetProperty(ref _dedicatedMemoryUsedMb, value);
    }

    public double DedicatedMemoryTotalMb
    {
        get => _dedicatedMemoryTotalMb;
        set => SetProperty(ref _dedicatedMemoryTotalMb, value);
    }

    public double DedicatedMemoryPercent
    {
        get => _dedicatedMemoryPercent;
        set => SetProperty(ref _dedicatedMemoryPercent, value);
    }

    public double SharedMemoryUsedMb
    {
        get => _sharedMemoryUsedMb;
        set => SetProperty(ref _sharedMemoryUsedMb, value);
    }

    public double Engine3DPercent
    {
        get => _engine3DPercent;
        set => SetProperty(ref _engine3DPercent, value);
    }

    public double EngineVideoDecodePercent
    {
        get => _engineVideoDecodePercent;
        set => SetProperty(ref _engineVideoDecodePercent, value);
    }

    public double EngineCopyPercent
    {
        get => _engineCopyPercent;
        set => SetProperty(ref _engineCopyPercent, value);
    }

    public double EngineComputePercent
    {
        get => _engineComputePercent;
        set => SetProperty(ref _engineComputePercent, value);
    }

    public string SparklineGeometry
    {
        get => _sparklineGeometry;
        set => SetProperty(ref _sparklineGeometry, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public GpuPanelViewModel()
    {
        for (int i = 0; i < MaxHistoryPoints; i++)
        {
            _history.AddLast(0.0);
        }
        UpdateSparkline();
    }

    public void ApplyMetrics(GpuMetrics metrics)
    {
        AdapterName = metrics.AdapterName;
        UtilizationPercent = metrics.UtilizationPercent;
        DedicatedMemoryUsedMb = metrics.DedicatedMemoryUsedMb;
        DedicatedMemoryTotalMb = metrics.DedicatedMemoryTotalMb;
        DedicatedMemoryPercent = metrics.DedicatedMemoryPercent;
        SharedMemoryUsedMb = metrics.SharedMemoryUsedMb;
        Engine3DPercent = metrics.Engine3DPercent;
        EngineVideoDecodePercent = metrics.EngineVideoDecodePercent;
        EngineCopyPercent = metrics.EngineCopyPercent;
        EngineComputePercent = metrics.EngineComputePercent;

        _history.AddLast(metrics.UtilizationPercent);
        while (_history.Count > MaxHistoryPoints)
        {
            _history.RemoveFirst();
        }

        UpdateSparkline();
        StatusText = $"D3D // {metrics.AdapterName.ToUpperInvariant()}";
    }

    private void UpdateSparkline()
    {
        if (_history.Count < 2) return;

        const double width = 360.0;
        const double height = 70.0;
        double step = width / (_history.Count - 1);

        var points = new List<string>();
        int index = 0;

        foreach (var val in _history)
        {
            double x = Math.Round(index * step, 1);
            // Invert Y: 100% -> top (0), 0% -> bottom (height)
            double normalized = Math.Clamp(val / 100.0, 0.0, 1.0);
            double y = Math.Round(height - (normalized * (height - 6)) - 3, 1);
            points.Add($"{x},{y}");
            index++;
        }

        SparklineGeometry = "M " + string.Join(" L ", points);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
