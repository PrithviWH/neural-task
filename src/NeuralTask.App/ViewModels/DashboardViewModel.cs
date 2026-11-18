using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private const double WaveformWidth = 800.0;
    private const double WaveformHeight = 150.0;

    private readonly PerformanceHistoryBuffer _cpuHistory = new(60);
    private readonly PerformanceHistoryBuffer _ramHistory = new(60);

    private readonly IHistoryStore _historyStore;
    private readonly IExportService _exportService;
    private readonly IHeuristicFlagEngine? _heuristicEngine;

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

    // Timeline Scrubber State (PRD §6.3 - 3.1)
    private bool _isScrubbing = false;
    private double _scrubberValue = 1.0;
    private string _timelineTimeDisplay = "LIVE // STREAMING";
    private string _timelineWindowDisplay = "-30m 00s";
    private bool _hasHistoricalSamples = false;
    private string _exportStatusMessage = string.Empty;

    private DateTimeOffset _lastLiveTimestamp = DateTimeOffset.Now;
    private SystemMetricsSnapshot? _latestSystemMetrics;
    private IReadOnlyList<ProcessSnapshot> _latestProcesses = Array.Empty<ProcessSnapshot>();
    private IReadOnlyDictionary<int, ProcessHeuristicResult>? _latestHeuristicResults;

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

    public bool IsScrubbing
    {
        get => _isScrubbing;
        set
        {
            if (SetProperty(ref _isScrubbing, value))
            {
                OnPropertyChanged(nameof(IsLive));
            }
        }
    }

    public bool IsLive => !_isScrubbing;

    public double ScrubberValue
    {
        get => _scrubberValue;
        set
        {
            if (SetProperty(ref _scrubberValue, value))
            {
                if (Math.Abs(value - 1.0) < 0.005)
                {
                    ResumeLive();
                }
                else
                {
                    ApplyScrubbedSample(value);
                }
            }
        }
    }

    public string TimelineTimeDisplay
    {
        get => _timelineTimeDisplay;
        private set => SetProperty(ref _timelineTimeDisplay, value);
    }

    public string TimelineWindowDisplay
    {
        get => _timelineWindowDisplay;
        private set => SetProperty(ref _timelineWindowDisplay, value);
    }

    public bool HasHistoricalSamples
    {
        get => _hasHistoricalSamples;
        private set => SetProperty(ref _hasHistoricalSamples, value);
    }

    public string ExportStatusMessage
    {
        get => _exportStatusMessage;
        private set => SetProperty(ref _exportStatusMessage, value);
    }

    public ICommand ResumeLiveCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<TopProcessItemViewModel> TopCpuProcesses { get; } = new();
    public ObservableCollection<TopProcessItemViewModel> TopRamProcesses { get; } = new();

    public DashboardViewModel(
        IHistoryStore? historyStore = null,
        IExportService? exportService = null,
        IHeuristicFlagEngine? heuristicEngine = null)
    {
        _historyStore = historyStore ?? new HistoryStore();
        _exportService = exportService ?? new ExportService();
        _heuristicEngine = heuristicEngine;

        ResumeLiveCommand = new RelayCommand(ResumeLive);
        ExportJsonCommand = new RelayCommand(async () => await ExportSnapshotAsync(ExportFormat.Json));
        ExportCsvCommand = new RelayCommand(async () => await ExportSnapshotAsync(ExportFormat.Csv));

        // Pre-populate 3 items in each top list to avoid layout jumping
        for (int i = 0; i < 3; i++)
        {
            TopCpuProcesses.Add(new TopProcessItemViewModel());
            TopRamProcesses.Add(new TopProcessItemViewModel());
        }
    }

    public void ApplyMetrics(IReadOnlyList<ProcessSnapshot> processes, SystemMetricsSnapshot system)
    {
        _latestProcesses = processes;
        _latestSystemMetrics = system;

        if (_heuristicEngine != null)
        {
            _latestHeuristicResults = _heuristicEngine.Evaluate(processes);
        }

        _lastLiveTimestamp = DateTimeOffset.Now;

        // 1. Ingest into historical store
        _historyStore.AddSample(_lastLiveTimestamp, system, processes, _latestHeuristicResults);
        HasHistoricalSamples = _historyStore.SampleCount > 1;

        // 2. Rolling Waveforms (always buffer live telemetry)
        _cpuHistory.AddSample(system.CpuPercent);
        _ramHistory.AddSample(system.MemoryPercent);
        CpuWaveformPoints = _cpuHistory.GeneratePoints(WaveformWidth, WaveformHeight, 100.0);
        RamWaveformPoints = _ramHistory.GeneratePoints(WaveformWidth, WaveformHeight, 100.0);

        // 3. System Hardware Info (static hardware specs)
        ProcessorModel = system.ProcessorName;
        OsVersion = system.OsVersion;
        UptimeDisplay = $"{system.Uptime.Days}d {system.Uptime.Hours:D2}h {system.Uptime.Minutes:D2}m {system.Uptime.Seconds:D2}s";

        // 4. If scrubbing, do not overwrite historical display values
        if (IsScrubbing) return;

        // 5. Update Live Displays
        CpuPercent = system.CpuPercent;
        CpuDisplay = $"{system.CpuPercent:F1}%";
        CpuCoresDisplay = $"{system.LogicalCoreCount} Cores // {system.ProcessorName}";

        double usedGb = system.UsedMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = system.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        RamDisplay = $"{usedGb:F1} GB";
        RamDetailDisplay = $"{system.MemoryPercent:F1}% of {totalGb:F1} GB";
        RamPercent = system.MemoryPercent;

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

        double totalNet = system.NetworkReceivedBytesPerSec + system.NetworkSentBytesPerSec;
        NetworkDisplay = FormatRate(totalNet);
        NetworkDetailDisplay = $"In: {FormatRate(system.NetworkReceivedBytesPerSec)} | Out: {FormatRate(system.NetworkSentBytesPerSec)}";
        NetworkProgress = Math.Clamp(totalNet / (10.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);

        ProcessCountDisplay = $"{system.ProcessCount} Processes";
        StatusPillText = $"KERNEL LINK: ACTIVE // {system.ProcessCount} TASKS";

        // Update timeline range bounds
        var history = _historyStore.GetHistory();
        if (history.Count > 0)
        {
            var span = _lastLiveTimestamp - history[0].Timestamp;
            TimelineWindowDisplay = span.TotalMinutes >= 1
                ? $"-{Math.Floor(span.TotalMinutes):F0}m {span.Seconds:D2}s"
                : $"-{span.Seconds:D2}s";
        }
        TimelineTimeDisplay = "LIVE // STREAMING";
        _scrubberValue = 1.0;
        OnPropertyChanged(nameof(ScrubberValue));

        UpdateTopOffenders(processes);
    }

    public void ApplyScrubbedSample(double fraction)
    {
        IsScrubbing = true;
        var sample = _historyStore.GetSampleByFraction(fraction);
        if (sample == null) return;

        // Apply historical sample values
        CpuPercent = sample.CpuPercent;
        CpuDisplay = $"{sample.CpuPercent:F1}%";
        CpuCoresDisplay = $"HISTORICAL TELEMETRY // {sample.Timestamp:HH:mm:ss}";

        double usedGb = sample.UsedMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = sample.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        RamDisplay = $"{usedGb:F1} GB";
        RamDetailDisplay = $"{sample.MemoryPercent:F1}% of {totalGb:F1} GB";
        RamPercent = sample.MemoryPercent;

        if (sample.ProcessIoBytesPerSec > 0)
        {
            DiskIoDisplay = FormatRate(sample.ProcessIoBytesPerSec);
            DiskIoDetailDisplay = "Historical Process File/Disk I/O";
            DiskIoProgress = Math.Clamp(sample.ProcessIoBytesPerSec / (50.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);
        }
        else
        {
            DiskIoDisplay = "0.0 KB/s";
            DiskIoDetailDisplay = "No Process File/Disk I/O Activity";
            DiskIoProgress = 0.0;
        }

        double totalNet = sample.NetworkReceivedBytesPerSec + sample.NetworkSentBytesPerSec;
        NetworkDisplay = FormatRate(totalNet);
        NetworkDetailDisplay = $"In: {FormatRate(sample.NetworkReceivedBytesPerSec)} | Out: {FormatRate(sample.NetworkSentBytesPerSec)}";
        NetworkProgress = Math.Clamp(totalNet / (10.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);

        ProcessCountDisplay = $"{sample.ProcessCount} Processes";

        var offset = _lastLiveTimestamp - sample.Timestamp;
        string offsetStr = offset.TotalMinutes >= 1
            ? $"-{Math.Floor(offset.TotalMinutes):F0}m {offset.Seconds:D2}s"
            : $"-{offset.Seconds:D2}s";

        TimelineTimeDisplay = $"SCRUBBING: {offsetStr} [{sample.Timestamp:HH:mm:ss}]";
        StatusPillText = $"HISTORICAL TELEMETRY // {sample.Timestamp:HH:mm:ss} ({offsetStr})";

        // Apply historical top offenders
        UpdateTopOffendersFromHistorical(sample.TopProcesses);
    }

    public void ResumeLive()
    {
        IsScrubbing = false;
        _scrubberValue = 1.0;
        OnPropertyChanged(nameof(ScrubberValue));

        if (_latestSystemMetrics != null)
        {
            ApplyMetrics(_latestProcesses, _latestSystemMetrics);
        }
    }

    public async Task ExportSnapshotAsync(ExportFormat format)
    {
        if (_latestSystemMetrics == null) return;

        string extension = format == ExportFormat.Json ? "json" : "csv";
        string filter = format == ExportFormat.Json ? "JSON Files (*.json)|*.json" : "CSV Files (*.csv)|*.csv";
        string defaultFileName = $"NeuralTask_Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";

        string targetPath;
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Export System Snapshot ({format})",
                Filter = filter,
                FileName = defaultFileName
            };

            bool? result = saveDialog.ShowDialog();
            if (result != true || string.IsNullOrEmpty(saveDialog.FileName))
            {
                return;
            }
            targetPath = saveDialog.FileName;
        }
        catch
        {
            // Fallback for non-interactive / headless contexts
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(dir);
            targetPath = Path.Combine(dir, defaultFileName);
        }

        try
        {
            await _exportService.ExportToFileAsync(
                targetPath,
                format,
                _latestSystemMetrics,
                _latestProcesses,
                _latestHeuristicResults
            );
            ExportStatusMessage = $"Exported {format} -> {Path.GetFileName(targetPath)}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void UpdateTopOffendersFromHistorical(IReadOnlyList<HistoricalProcessMetrics> topProcesses)
    {
        if (topProcesses.Count == 0) return;

        var topCpu = topProcesses.OrderByDescending(p => p.CpuPercent).Take(3).ToList();
        for (int i = 0; i < 3; i++)
        {
            if (i < topCpu.Count)
            {
                var p = topCpu[i];
                TopCpuProcesses[i].Name = p.Name;
                TopCpuProcesses[i].Pid = p.Pid;
                TopCpuProcesses[i].Subtext = p.FlagReason != null ? $"PID {p.Pid} • {p.FlagReason}" : $"PID {p.Pid}";
                TopCpuProcesses[i].ValueDisplay = $"{p.CpuPercent:F2}%";
            }
        }

        var topRam = topProcesses.OrderByDescending(p => p.RamBytes).Take(3).ToList();
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
