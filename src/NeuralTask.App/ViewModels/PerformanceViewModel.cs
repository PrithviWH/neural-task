using System;
using System.Windows.Media;
using NeuralTask.Core.Models;

namespace NeuralTask.App.ViewModels;

public class PerformanceViewModel : ViewModelBase
{
    private const double ChartWidth = 800.0;
    private const double ChartHeight = 150.0;

    private readonly PerformanceHistoryBuffer _cpuBuffer = new(60);
    private readonly PerformanceHistoryBuffer _memoryBuffer = new(60);
    private readonly PerformanceHistoryBuffer _ioBuffer = new(60);
    private readonly PerformanceHistoryBuffer _networkBuffer = new(60);

    private PointCollection _cpuPoints = new();
    private PointCollection _memoryPoints = new();
    private PointCollection _ioPoints = new();
    private PointCollection _networkPoints = new();

    private string _currentCpuText = "0.0%";
    private string _currentMemoryText = "0.0%";
    private string _currentIoText = "0.0 KB/s";
    private string _currentNetworkText = "0.0 KB/s";

    public PointCollection CpuPoints
    {
        get => _cpuPoints;
        private set => SetProperty(ref _cpuPoints, value);
    }

    public PointCollection MemoryPoints
    {
        get => _memoryPoints;
        private set => SetProperty(ref _memoryPoints, value);
    }

    public PointCollection IoPoints
    {
        get => _ioPoints;
        private set => SetProperty(ref _ioPoints, value);
    }

    public PointCollection NetworkPoints
    {
        get => _networkPoints;
        private set => SetProperty(ref _networkPoints, value);
    }

    public string CurrentCpuText
    {
        get => _currentCpuText;
        private set => SetProperty(ref _currentCpuText, value);
    }

    public string CurrentMemoryText
    {
        get => _currentMemoryText;
        private set => SetProperty(ref _currentMemoryText, value);
    }

    public string CurrentIoText
    {
        get => _currentIoText;
        private set => SetProperty(ref _currentIoText, value);
    }

    public string CurrentNetworkText
    {
        get => _currentNetworkText;
        private set => SetProperty(ref _currentNetworkText, value);
    }

    public void ApplyMetrics(SystemMetricsSnapshot system)
    {
        _cpuBuffer.AddSample(system.CpuPercent);
        _memoryBuffer.AddSample(system.MemoryPercent);
        _ioBuffer.AddSample(system.ProcessIoBytesPerSec);

        double totalNet = system.NetworkReceivedBytesPerSec + system.NetworkSentBytesPerSec;
        _networkBuffer.AddSample(totalNet);

        CpuPoints = _cpuBuffer.GeneratePoints(ChartWidth, ChartHeight, 100.0);
        MemoryPoints = _memoryBuffer.GeneratePoints(ChartWidth, ChartHeight, 100.0);
        IoPoints = _ioBuffer.GeneratePoints(ChartWidth, ChartHeight, Math.Max(1024.0 * 1024.0, system.ProcessIoBytesPerSec * 1.5));
        NetworkPoints = _networkBuffer.GeneratePoints(ChartWidth, ChartHeight, Math.Max(500.0 * 1024.0, totalNet * 1.5));

        CurrentCpuText = $"{system.CpuPercent:F1}%";
        CurrentMemoryText = $"{system.MemoryPercent:F1}%";
        CurrentIoText = FormatRate(system.ProcessIoBytesPerSec);
        CurrentNetworkText = FormatRate(totalNet);
    }

    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0.0 KB/s";
        double kb = bytesPerSec / 1024.0;
        if (kb >= 1024.0)
        {
            return $"{kb / 1024.0:F1} MB/s";
        }
        return $"{kb:F1} KB/s";
    }
}
