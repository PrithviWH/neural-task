using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using NeuralTask.Core.Models;

namespace NeuralTask.App.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private const double WaveformWidth = 800.0;
    private const double WaveformHeight = 150.0;

    private readonly PerformanceHistoryBuffer _cpuHistory = new(60);
    private readonly PerformanceHistoryBuffer _ramHistory = new(60);

    private double _cpuPercent;
    private string _cpuDisplay = "0.0%";
    private string _cpuCoresDisplay = $"{Environment.ProcessorCount} Cores Active";

    private string _ramDisplay = "—";
    private string _ramDetailDisplay = "—";
    private double _ramPercent;

    private string _diskIoDisplay = "—";
    private string _diskIoDetailDisplay = "Physical Disk: — (Requires Admin IOCTL)";
    private double _diskIoProgress;

    private string _networkDisplay = "—";
    private string _networkDetailDisplay = "In: 0.0 KB/s | Out: 0.0 KB/s";
    private double _networkProgress;

    private string _processorModel = "Detecting Processor...";
    private string _osVersion = string.Empty;
    private string _uptimeDisplay = "00:00:00";
    private string _processCountDisplay = "0 Processes";
    private string _statusPillText = "KERNEL LINK: INITIALIZING";

    private PointCollection _cpuWaveformPoints = new();
    private PointCollection _ramWaveformPoints = new();

    public double CpuPercent
    {
        get => _cpuPercent;
        private set => SetProperty(ref _cpuPercent, value);
    }

    public string CpuDisplay
    {
        get => _cpuDisplay;
        private set => SetProperty(ref _cpuDisplay, value);
    }

    public string CpuCoresDisplay
    {
        get => _cpuCoresDisplay;
        private set => SetProperty(ref _cpuCoresDisplay, value);
    }

    public string RamDisplay
    {
        get => _ramDisplay;
        private set => SetProperty(ref _ramDisplay, value);
    }

    public string RamDetailDisplay
    {
        get => _ramDetailDisplay;
        private set => SetProperty(ref _ramDetailDisplay, value);
    }

    public double RamPercent
    {
        get => _ramPercent;
        private set => SetProperty(ref _ramPercent, value);
    }

    public string DiskIoDisplay
    {
        get => _diskIoDisplay;
        private set => SetProperty(ref _diskIoDisplay, value);
    }

    public string DiskIoDetailDisplay
    {
        get => _diskIoDetailDisplay;
        private set => SetProperty(ref _diskIoDetailDisplay, value);
    }

    public double DiskIoProgress
    {
        get => _diskIoProgress;
        private set => SetProperty(ref _diskIoProgress, value);
    }

    public string NetworkDisplay
    {
        get => _networkDisplay;
        private set => SetProperty(ref _networkDisplay, value);
    }

    public string NetworkDetailDisplay
    {
        get => _networkDetailDisplay;
        private set => SetProperty(ref _networkDetailDisplay, value);
    }

    public double NetworkProgress
    {
        get => _networkProgress;
        private set => SetProperty(ref _networkProgress, value);
    }

    public string ProcessorModel
    {
        get => _processorModel;
        private set => SetProperty(ref _processorModel, value);
    }

    public string OsVersion
    {
        get => _osVersion;
        private set => SetProperty(ref _osVersion, value);
    }

    public string UptimeDisplay
    {
        get => _uptimeDisplay;
        private set => SetProperty(ref _uptimeDisplay, value);
    }

    public string ProcessCountDisplay
    {
        get => _processCountDisplay;
        private set => SetProperty(ref _processCountDisplay, value);
    }

    public string StatusPillText
    {
        get => _statusPillText;
        private set => SetProperty(ref _statusPillText, value);
    }

    public PointCollection CpuWaveformPoints
    {
        get => _cpuWaveformPoints;
        private set => SetProperty(ref _cpuWaveformPoints, value);
    }

    public PointCollection RamWaveformPoints
    {
        get => _ramWaveformPoints;
        private set => SetProperty(ref _ramWaveformPoints, value);
    }

    public ObservableCollection<TopProcessItemViewModel> TopCpuProcesses { get; } = new();
    public ObservableCollection<TopProcessItemViewModel> TopRamProcesses { get; } = new();

    public DashboardViewModel()
    {
        // Pre-populate 3 items in each top list to avoid layout jumping
        for (int i = 0; i < 3; i++)
        {
            TopCpuProcesses.Add(new TopProcessItemViewModel());
            TopRamProcesses.Add(new TopProcessItemViewModel());
        }
    }

    public void ApplyMetrics(IReadOnlyList<ProcessSnapshot> processes, SystemMetricsSnapshot system)
    {
        // 1. System CPU
        CpuPercent = system.CpuPercent;
        CpuDisplay = $"{system.CpuPercent:F1}%";
        CpuCoresDisplay = $"{system.LogicalCoreCount} Cores // {system.ProcessorName}";

        // 2. System RAM
        double usedGb = system.UsedMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = system.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        RamDisplay = $"{usedGb:F1} GB";
        RamDetailDisplay = $"{system.MemoryPercent:F1}% of {totalGb:F1} GB";
        RamPercent = system.MemoryPercent;

        // 3. Process File / Disk I/O (Semantically labeled per prompt)
        if (system.ProcessIoBytesPerSec > 0)
        {
            DiskIoDisplay = FormatRate(system.ProcessIoBytesPerSec);
            DiskIoDetailDisplay = "Active Process File/Disk I/O Rate";
            DiskIoProgress = Math.Clamp(system.ProcessIoBytesPerSec / (50.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);
        }
        else
        {
            DiskIoDisplay = "0.0 KB/s";
            DiskIoDetailDisplay = "No Process File/Disk I/O Activity";
            DiskIoProgress = 0.0;
        }

        // 4. System-Wide Network (Genuine IPv4 interface throughput)
        double totalNet = system.NetworkReceivedBytesPerSec + system.NetworkSentBytesPerSec;
        NetworkDisplay = FormatRate(totalNet);
        NetworkDetailDisplay = $"In: {FormatRate(system.NetworkReceivedBytesPerSec)} | Out: {FormatRate(system.NetworkSentBytesPerSec)}";
        NetworkProgress = Math.Clamp(totalNet / (10.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);

        // 5. System Info
        ProcessorModel = system.ProcessorName;
        OsVersion = system.OsVersion;
        UptimeDisplay = $"{system.Uptime.Days}d {system.Uptime.Hours:D2}h {system.Uptime.Minutes:D2}m {system.Uptime.Seconds:D2}s";
        ProcessCountDisplay = $"{system.ProcessCount} Processes";
        StatusPillText = $"KERNEL LINK: ACTIVE // {system.ProcessCount} TASKS";

        // 6. Rolling Waveforms
        _cpuHistory.AddSample(system.CpuPercent);
        _ramHistory.AddSample(system.MemoryPercent);

        CpuWaveformPoints = _cpuHistory.GeneratePoints(WaveformWidth, WaveformHeight, 100.0);
        RamWaveformPoints = _ramHistory.GeneratePoints(WaveformWidth, WaveformHeight, 100.0);

        // 7. Top Offenders Updates
        UpdateTopOffenders(processes);
    }

    private void UpdateTopOffenders(IReadOnlyList<ProcessSnapshot> processes)
    {
        if (processes.Count == 0) return;

        // Top CPU (excluding System Idle PID 0)
        var topCpu = processes
            .Where(p => p.Pid != 0 && !p.IsAccessDenied)
            .OrderByDescending(p => p.CpuPercent)
            .Take(3)
            .ToList();

        for (int i = 0; i < 3; i++)
        {
            if (i < topCpu.Count)
            {
                var p = topCpu[i];
                TopCpuProcesses[i].Name = p.Name;
                TopCpuProcesses[i].Pid = p.Pid;
                TopCpuProcesses[i].Subtext = $"PID {p.Pid}";
                TopCpuProcesses[i].ValueDisplay = $"{p.CpuPercent:F2}%";
            }
        }

        // Top RAM
        var topRam = processes
            .Where(p => p.Pid != 0)
            .OrderByDescending(p => p.RamBytes)
            .Take(3)
            .ToList();

        for (int i = 0; i < 3; i++)
        {
            if (i < topRam.Count)
            {
                var p = topRam[i];
                TopRamProcesses[i].Name = p.Name;
                TopRamProcesses[i].Pid = p.Pid;
                TopRamProcesses[i].Subtext = $"PID {p.Pid}";
                double mb = p.RamBytes / (1024.0 * 1024.0);
                TopRamProcesses[i].ValueDisplay = mb >= 1024.0 ? $"{mb / 1024.0:F2} GB" : $"{mb:F0} MB";
            }
        }
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
