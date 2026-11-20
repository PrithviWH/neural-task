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

public class DashboardViewModel : ViewModelBase, IDisposable
{
    private double _chartViewportWidth = 1000.0;
    private double _chartViewportHeight = 180.0;

    // Rolling 60-second buffers with 4s delay margin for continuous 60 FPS sub-pixel scrolling
    private readonly PerformanceHistoryBuffer _cpuHistory = new(64);
    private readonly PerformanceHistoryBuffer _ramHistory = new(64);
    private readonly PerformanceHistoryBuffer _gpuHistory = new(64);
    private readonly PerformanceHistoryBuffer _diskHistory = new(64);
    private readonly PerformanceHistoryBuffer _netHistory = new(64);

    private readonly IHistoryStore _historyStore;
    private readonly IExportService _exportService;
    private readonly IHeuristicFlagEngine? _heuristicEngine;
    private readonly IGpuMetricsService _gpuService;
    private readonly IStorageInfoService _storageService;
    private readonly INetworkInfoService _networkService;
    private readonly IStartupScoreEngine? _startupEngine;

    // 1. System Overview Metrics
    private double _cpuPercent;
    private string _cpuDisplay = "0.0%";
    private string _cpuTrendDisplay = "0.0%";
    private string _cpuCoresDisplay = $"{Environment.ProcessorCount} Cores Active";
    private double _prevCpuPercent;

    private string _ramDisplay = "—";
    private string _ramDetailDisplay = "—";
    private string _ramTrendDisplay = "0.0%";
    private double _ramPercent;

    private string _gpuDisplay = "0.0%";
    private double _gpuPercent;
    private string _gpuStatusDisplay = "GPU Active";
    private string _gpuSubtext = "DirectX Hardware GPU";
    private bool _isGpuSupported = true;

    private string _processCountDisplay = "0 Processes";
    private string _processCountTrend = "Optimal";
    private string _processSubtext = "Kernel threads & handles active";
    private double _processCountProgress = 20.0;

    // Sectioned Statistics Dashboard Fields - CPU Section
    private double? _cpuClockMhz = 3200.0;
    private string _cpuClockDisplay = "3,200 MHz";
    private double? _cpuPowerWatts = 35.0;
    private string _cpuPowerDisplay = "35.0 W";
    private double? _cpuTemperatureC = 55.0;
    private string _cpuTemperatureDisplay = "55.0 °C";
    private string _cpuTemperatureColor = "#4ADE80";
    private string _cpuUtilizationDisplay = "0.0%";
    private string _cpuUtilizationColor = "#8389FF";

    // Sectioned Statistics Dashboard Fields - GPU Section
    private double? _gpuClockMhz = 1425.0;
    private string _gpuClockDisplay = "1,425 MHz";
    private double? _gpuPowerWatts = 28.0;
    private string _gpuPowerDisplay = "28.0 W";
    private double? _gpuTemperatureC = 65.0;
    private string _gpuTemperatureDisplay = "65.0 °C";
    private string _gpuTemperatureColor = "#2DD4C6";
    private double? _gpuVoltageV = 0.825;
    private string _gpuVoltageDisplay = "0.825 V";
    private double? _vramClockMhz = 6001.0;
    private string _vramClockDisplay = "6,001 MHz";
    private string _gpuUtilizationDisplay = "0.0%";
    private string _gpuUtilizationColor = "#8389FF";
    private string _gpuAdapterTitle = "DirectX Hardware GPU";

    // 2. Storage & Network Metrics
    private string _diskIoDisplay = "0.0 KB/s";
    private string _diskIoDetailDisplay = "Active process read + write";
    private double _diskIoProgress;
    private string _primaryDriveName = "C:\\";
    private string _primaryDriveLabel = "System OS";
    private string _primaryDriveCapacityDisplay = "0 GB / 0 GB";
    private double _primaryDrivePercent;
    private string _storagePartitionsSummary = "Checking storage...";

    private string _networkDisplay = "0.0 KB/s";
    private string _networkDetailDisplay = "In: 0.0 KB/s • Out: 0.0 KB/s";
    private string _networkDownloadDisplay = "0.0 KB/s";
    private string _networkUploadDisplay = "0.0 KB/s";
    private double _networkProgress;
    private string _networkAdapterName = "Network";
    private string _networkAdapterDescription = "Active adapter";
    private string _networkInterfaceType = "Ethernet / Wi-Fi";
    private string _networkLinkSpeed = "100 Mbps";

    // 3. System Intelligence
    private string _processorModel = "Detecting Processor...";
    private string _osVersion = string.Empty;
    private string _uptimeDisplay = "00:00:00";
    private string _statusPillText = "System online";
    private string _healthStatusText = "Optimal";
    private string _healthStatusMessage = "All hardware subsystems operating within normal parameters.";
    private string _recentSpikesDisplay = "No spikes detected in rolling window";
    private string _startupImpactSummary = "Startup impact scoring active";
    private string _installedMemoryDisplay = "0.0 GB Physical RAM";

    // 4. Advanced Telemetry Navigation & Geometries
    private string _selectedMetric = "Overview"; // Overview, Cpu, Memory, Gpu, Disk, Network
    private string _selectedRange = "60s";      // 60s, 5m, 15m, 30m
    private string _currentMetricDisplay = "Overview • Live Stream";

    private PointCollection _cpuWaveformPoints = new();
    private PointCollection _ramWaveformPoints = new();
    private Geometry _cpuCurveGeometry = Geometry.Empty;
    private Geometry _cpuAreaGeometry = Geometry.Empty;
    private Geometry _ramCurveGeometry = Geometry.Empty;
    private Geometry _ramAreaGeometry = Geometry.Empty;

    private Geometry _primaryCurveGeometry = Geometry.Empty;
    private Geometry _primaryAreaGeometry = Geometry.Empty;
    private Geometry _secondaryCurveGeometry = Geometry.Empty;
    private Geometry _secondaryAreaGeometry = Geometry.Empty;
    private bool _isDualMetric = true;

    // Multi-Channel Telemetry Geometries for Individual Metric Cards
    private Geometry _gpuCurveGeometry = Geometry.Empty;
    private Geometry _gpuAreaGeometry = Geometry.Empty;
    private Geometry _diskCurveGeometry = Geometry.Empty;
    private Geometry _diskAreaGeometry = Geometry.Empty;
    private Geometry _netCurveGeometry = Geometry.Empty;
    private Geometry _netAreaGeometry = Geometry.Empty;

    private double _cardViewportWidth = 320.0;
    private double _cardViewportHeight = 95.0;

    private string _cpuPeakDisplay = "0.0%";
    private string _ramPercentDisplay = "0.0%";
    private string _diskPeakDisplay = "0.0 MB/s";
    private string _netPeakDisplay = "0.0 MB/s";

    // Live Channel Telemetry Inspector State
    private double _primaryLiveY = 100.0;
    private double _secondaryLiveY = 50.0;
    private string _activeChannelName = "System Telemetry";
    private string _activeChannelTag = "Dual Live Stream";
    private string _activeChannelValue = "0.0%";
    private string _activeChannelSubtext = "Kernel hardware sensors";
    private string _activeChannelPeak = "0.0%";
    private string _activeChannelAverage = "0.0%";
    private string _activeChannelMin = "0.0%";
    private string _activeChannelStatus = "Optimal Workload";
    private string _activeChannelColor = "#8389FF";
    private bool _isOverviewChannel = true;

    // 5. Timeline Scrubber State
    private bool _isScrubbing = false;
    private double _scrubberValue = 1.0;
    private string _timelineTimeDisplay = "Live • Streaming";
    private string _timelineWindowDisplay = "-30m 00s";
    private bool _hasHistoricalSamples = false;
    private string _exportStatusMessage = string.Empty;

    private DateTimeOffset _lastLiveTimestamp = DateTimeOffset.Now;
    private SystemMetricsSnapshot? _latestSystemMetrics;
    private GpuMetricsSnapshot? _latestGpuMetrics;
    private IReadOnlyList<ProcessSnapshot> _latestProcesses = Array.Empty<ProcessSnapshot>();

    // Public Properties - Section 1: System Overview
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

    public string CpuTrendDisplay
    {
        get => _cpuTrendDisplay;
        private set => SetProperty(ref _cpuTrendDisplay, value);
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

    public string RamTrendDisplay
    {
        get => _ramTrendDisplay;
        private set => SetProperty(ref _ramTrendDisplay, value);
    }

    public double RamPercent
    {
        get => _ramPercent;
        private set => SetProperty(ref _ramPercent, value);
    }

    public string GpuDisplay
    {
        get => _gpuDisplay;
        private set => SetProperty(ref _gpuDisplay, value);
    }

    public double GpuPercent
    {
        get => _gpuPercent;
        private set => SetProperty(ref _gpuPercent, value);
    }

    public string GpuStatusDisplay
    {
        get => _gpuStatusDisplay;
        private set => SetProperty(ref _gpuStatusDisplay, value);
    }

    public string GpuSubtext
    {
        get => _gpuSubtext;
        private set => SetProperty(ref _gpuSubtext, value);
    }

    public bool IsGpuSupported
    {
        get => _isGpuSupported;
        private set => SetProperty(ref _isGpuSupported, value);
    }

    public string ProcessCountDisplay
    {
        get => _processCountDisplay;
        private set => SetProperty(ref _processCountDisplay, value);
    }

    public string ProcessCountTrend
    {
        get => _processCountTrend;
        private set => SetProperty(ref _processCountTrend, value);
    }

    public string ProcessSubtext
    {
        get => _processSubtext;
        private set => SetProperty(ref _processSubtext, value);
    }

    public double ProcessCountProgress
    {
        get => _processCountProgress;
        private set => SetProperty(ref _processCountProgress, value);
    }

    #region Sectioned Statistics Properties - CPU Section
    public double? CpuClockMhz
    {
        get => _cpuClockMhz;
        private set => SetProperty(ref _cpuClockMhz, value);
    }

    public string CpuClockDisplay
    {
        get => _cpuClockDisplay;
        private set => SetProperty(ref _cpuClockDisplay, value);
    }

    public double? CpuPowerWatts
    {
        get => _cpuPowerWatts;
        private set => SetProperty(ref _cpuPowerWatts, value);
    }

    public string CpuPowerDisplay
    {
        get => _cpuPowerDisplay;
        private set => SetProperty(ref _cpuPowerDisplay, value);
    }

    public double? CpuTemperatureC
    {
        get => _cpuTemperatureC;
        private set => SetProperty(ref _cpuTemperatureC, value);
    }

    public string CpuTemperatureDisplay
    {
        get => _cpuTemperatureDisplay;
        private set => SetProperty(ref _cpuTemperatureDisplay, value);
    }

    public string CpuTemperatureColor
    {
        get => _cpuTemperatureColor;
        private set => SetProperty(ref _cpuTemperatureColor, value);
    }

    public string CpuUtilizationDisplay
    {
        get => _cpuUtilizationDisplay;
        private set => SetProperty(ref _cpuUtilizationDisplay, value);
    }

    public string CpuUtilizationColor
    {
        get => _cpuUtilizationColor;
        private set => SetProperty(ref _cpuUtilizationColor, value);
    }
    #endregion

    #region Sectioned Statistics Properties - GPU Section
    public double? GpuClockMhz
    {
        get => _gpuClockMhz;
        private set => SetProperty(ref _gpuClockMhz, value);
    }

    public string GpuClockDisplay
    {
        get => _gpuClockDisplay;
        private set => SetProperty(ref _gpuClockDisplay, value);
    }

    public double? GpuPowerWatts
    {
        get => _gpuPowerWatts;
        private set => SetProperty(ref _gpuPowerWatts, value);
    }

    public string GpuPowerDisplay
    {
        get => _gpuPowerDisplay;
        private set => SetProperty(ref _gpuPowerDisplay, value);
    }

    public double? GpuTemperatureC
    {
        get => _gpuTemperatureC;
        private set => SetProperty(ref _gpuTemperatureC, value);
    }

    public string GpuTemperatureDisplay
    {
        get => _gpuTemperatureDisplay;
        private set => SetProperty(ref _gpuTemperatureDisplay, value);
    }

    public string GpuTemperatureColor
    {
        get => _gpuTemperatureColor;
        private set => SetProperty(ref _gpuTemperatureColor, value);
    }

    public double? GpuVoltageV
    {
        get => _gpuVoltageV;
        private set => SetProperty(ref _gpuVoltageV, value);
    }

    public string GpuVoltageDisplay
    {
        get => _gpuVoltageDisplay;
        private set => SetProperty(ref _gpuVoltageDisplay, value);
    }

    public double? VramClockMhz
    {
        get => _vramClockMhz;
        private set => SetProperty(ref _vramClockMhz, value);
    }

    public string VramClockDisplay
    {
        get => _vramClockDisplay;
        private set => SetProperty(ref _vramClockDisplay, value);
    }

    public string GpuUtilizationDisplay
    {
        get => _gpuUtilizationDisplay;
        private set => SetProperty(ref _gpuUtilizationDisplay, value);
    }

    public string GpuUtilizationColor
    {
        get => _gpuUtilizationColor;
        private set => SetProperty(ref _gpuUtilizationColor, value);
    }

    public string GpuAdapterTitle
    {
        get => _gpuAdapterTitle;
        private set => SetProperty(ref _gpuAdapterTitle, value);
    }
    #endregion

    // Public Properties - Section 2: Storage & Network
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

    public string PrimaryDriveName
    {
        get => _primaryDriveName;
        private set => SetProperty(ref _primaryDriveName, value);
    }

    public string PrimaryDriveLabel
    {
        get => _primaryDriveLabel;
        private set => SetProperty(ref _primaryDriveLabel, value);
    }

    public string PrimaryDriveCapacityDisplay
    {
        get => _primaryDriveCapacityDisplay;
        private set => SetProperty(ref _primaryDriveCapacityDisplay, value);
    }

    public double PrimaryDrivePercent
    {
        get => _primaryDrivePercent;
        private set => SetProperty(ref _primaryDrivePercent, value);
    }

    public string StoragePartitionsSummary
    {
        get => _storagePartitionsSummary;
        private set => SetProperty(ref _storagePartitionsSummary, value);
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

    public string NetworkDownloadDisplay
    {
        get => _networkDownloadDisplay;
        private set => SetProperty(ref _networkDownloadDisplay, value);
    }

    public string NetworkUploadDisplay
    {
        get => _networkUploadDisplay;
        private set => SetProperty(ref _networkUploadDisplay, value);
    }

    public double NetworkProgress
    {
        get => _networkProgress;
        private set => SetProperty(ref _networkProgress, value);
    }

    public string NetworkAdapterName
    {
        get => _networkAdapterName;
        private set => SetProperty(ref _networkAdapterName, value);
    }

    public string NetworkAdapterDescription
    {
        get => _networkAdapterDescription;
        private set => SetProperty(ref _networkAdapterDescription, value);
    }

    public string NetworkInterfaceType
    {
        get => _networkInterfaceType;
        private set => SetProperty(ref _networkInterfaceType, value);
    }

    public string NetworkLinkSpeed
    {
        get => _networkLinkSpeed;
        private set => SetProperty(ref _networkLinkSpeed, value);
    }

    // Public Properties - Section 3: System Intelligence
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

    public string StatusPillText
    {
        get => _statusPillText;
        private set => SetProperty(ref _statusPillText, value);
    }

    public string HealthStatusText
    {
        get => _healthStatusText;
        private set => SetProperty(ref _healthStatusText, value);
    }

    public string HealthStatusMessage
    {
        get => _healthStatusMessage;
        private set => SetProperty(ref _healthStatusMessage, value);
    }

    public string RecentSpikesDisplay
    {
        get => _recentSpikesDisplay;
        private set => SetProperty(ref _recentSpikesDisplay, value);
    }

    public string StartupImpactSummary
    {
        get => _startupImpactSummary;
        private set => SetProperty(ref _startupImpactSummary, value);
    }

    public string InstalledMemoryDisplay
    {
        get => _installedMemoryDisplay;
        private set => SetProperty(ref _installedMemoryDisplay, value);
    }

    // Public Properties - Section 4: Advanced Telemetry Chart Selection
    public string SelectedMetric
    {
        get => _selectedMetric;
        set
        {
            if (SetProperty(ref _selectedMetric, value))
            {
                UpdateChartDisplay();
            }
        }
    }

    public string SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (SetProperty(ref _selectedRange, value))
            {
                UpdateChartDisplay();
            }
        }
    }

    public double ChartViewportWidth
    {
        get => _chartViewportWidth;
        set
        {
            if (value > 50 && Math.Abs(_chartViewportWidth - value) > 1.0)
            {
                if (SetProperty(ref _chartViewportWidth, value))
                {
                    UpdateChartDisplay();
                }
            }
        }
    }

    public double ChartViewportHeight
    {
        get => _chartViewportHeight;
        set
        {
            if (value > 50 && Math.Abs(_chartViewportHeight - value) > 1.0)
            {
                if (SetProperty(ref _chartViewportHeight, value))
                {
                    UpdateChartDisplay();
                }
            }
        }
    }

    public string CurrentMetricDisplay
    {
        get => _currentMetricDisplay;
        private set => SetProperty(ref _currentMetricDisplay, value);
    }

    public Geometry PrimaryCurveGeometry
    {
        get => _primaryCurveGeometry;
        private set => SetProperty(ref _primaryCurveGeometry, value);
    }

    public Geometry PrimaryAreaGeometry
    {
        get => _primaryAreaGeometry;
        private set => SetProperty(ref _primaryAreaGeometry, value);
    }

    public Geometry SecondaryCurveGeometry
    {
        get => _secondaryCurveGeometry;
        private set => SetProperty(ref _secondaryCurveGeometry, value);
    }

    public Geometry SecondaryAreaGeometry
    {
        get => _secondaryAreaGeometry;
        private set => SetProperty(ref _secondaryAreaGeometry, value);
    }

    public bool IsDualMetric
    {
        get => _isDualMetric;
        private set => SetProperty(ref _isDualMetric, value);
    }

    public double PrimaryLiveY
    {
        get => _primaryLiveY;
        private set => SetProperty(ref _primaryLiveY, value);
    }

    public double SecondaryLiveY
    {
        get => _secondaryLiveY;
        private set => SetProperty(ref _secondaryLiveY, value);
    }

    public string ActiveChannelName
    {
        get => _activeChannelName;
        private set => SetProperty(ref _activeChannelName, value);
    }

    public string ActiveChannelTag
    {
        get => _activeChannelTag;
        private set => SetProperty(ref _activeChannelTag, value);
    }

    public string ActiveChannelValue
    {
        get => _activeChannelValue;
        private set => SetProperty(ref _activeChannelValue, value);
    }

    public string ActiveChannelSubtext
    {
        get => _activeChannelSubtext;
        private set => SetProperty(ref _activeChannelSubtext, value);
    }

    public string ActiveChannelPeak
    {
        get => _activeChannelPeak;
        private set => SetProperty(ref _activeChannelPeak, value);
    }

    public string ActiveChannelAverage
    {
        get => _activeChannelAverage;
        private set => SetProperty(ref _activeChannelAverage, value);
    }

    public string ActiveChannelMin
    {
        get => _activeChannelMin;
        private set => SetProperty(ref _activeChannelMin, value);
    }

    public string ActiveChannelStatus
    {
        get => _activeChannelStatus;
        private set => SetProperty(ref _activeChannelStatus, value);
    }

    public string ActiveChannelColor
    {
        get => _activeChannelColor;
        private set => SetProperty(ref _activeChannelColor, value);
    }

    public bool IsOverviewChannel
    {
        get => _isOverviewChannel;
        private set => SetProperty(ref _isOverviewChannel, value);
    }

    // Preserved for backward compatibility
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

    public Geometry CpuCurveGeometry
    {
        get => _cpuCurveGeometry;
        private set => SetProperty(ref _cpuCurveGeometry, value);
    }

    public Geometry CpuAreaGeometry
    {
        get => _cpuAreaGeometry;
        private set => SetProperty(ref _cpuAreaGeometry, value);
    }

    public Geometry RamCurveGeometry
    {
        get => _ramCurveGeometry;
        private set => SetProperty(ref _ramCurveGeometry, value);
    }

    public Geometry RamAreaGeometry
    {
        get => _ramAreaGeometry;
        private set => SetProperty(ref _ramAreaGeometry, value);
    }

    public Geometry GpuCurveGeometry
    {
        get => _gpuCurveGeometry;
        private set => SetProperty(ref _gpuCurveGeometry, value);
    }

    public Geometry GpuAreaGeometry
    {
        get => _gpuAreaGeometry;
        private set => SetProperty(ref _gpuAreaGeometry, value);
    }

    public Geometry DiskCurveGeometry
    {
        get => _diskCurveGeometry;
        private set => SetProperty(ref _diskCurveGeometry, value);
    }

    public Geometry DiskAreaGeometry
    {
        get => _diskAreaGeometry;
        private set => SetProperty(ref _diskAreaGeometry, value);
    }

    public Geometry NetCurveGeometry
    {
        get => _netCurveGeometry;
        private set => SetProperty(ref _netCurveGeometry, value);
    }

    public Geometry NetAreaGeometry
    {
        get => _netAreaGeometry;
        private set => SetProperty(ref _netAreaGeometry, value);
    }

    public double CardViewportWidth
    {
        get => _cardViewportWidth;
        set
        {
            if (value > 30 && Math.Abs(_cardViewportWidth - value) > 1.0)
            {
                if (SetProperty(ref _cardViewportWidth, value))
                {
                    UpdateChartDisplay();
                }
            }
        }
    }

    public double CardViewportHeight
    {
        get => _cardViewportHeight;
        set
        {
            if (value > 20 && Math.Abs(_cardViewportHeight - value) > 1.0)
            {
                if (SetProperty(ref _cardViewportHeight, value))
                {
                    UpdateChartDisplay();
                }
            }
        }
    }

    public string CpuPeakDisplay
    {
        get => _cpuPeakDisplay;
        private set => SetProperty(ref _cpuPeakDisplay, value);
    }

    public string RamPercentDisplay
    {
        get => _ramPercentDisplay;
        private set => SetProperty(ref _ramPercentDisplay, value);
    }

    public string DiskPeakDisplay
    {
        get => _diskPeakDisplay;
        private set => SetProperty(ref _diskPeakDisplay, value);
    }

    public string NetPeakDisplay
    {
        get => _netPeakDisplay;
        private set => SetProperty(ref _netPeakDisplay, value);
    }

    // Timeline Scrubber
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

    // Commands
    public ICommand ResumeLiveCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand SelectMetricCommand { get; }
    public ICommand SelectRangeCommand { get; }

    // Section 3: Top Resource Consumers Collections (Top 5 each)
    public ObservableCollection<TopProcessItemViewModel> TopCpuProcesses { get; } = new();
    public ObservableCollection<TopProcessItemViewModel> TopRamProcesses { get; } = new();
    public ObservableCollection<TopProcessItemViewModel> TopGpuProcesses { get; } = new();

    public DashboardViewModel(
        IHistoryStore? historyStore = null,
        IExportService? exportService = null,
        IHeuristicFlagEngine? heuristicEngine = null,
        IGpuMetricsService? gpuService = null,
        IStorageInfoService? storageService = null,
        INetworkInfoService? networkService = null,
        IStartupScoreEngine? startupEngine = null)
    {
        _historyStore = historyStore ?? new HistoryStore();
        _exportService = exportService ?? new ExportService();
        _heuristicEngine = heuristicEngine;
        _gpuService = gpuService ?? GpuMetricsService.Instance;
        _storageService = storageService ?? StorageInfoService.Instance;
        _networkService = networkService ?? NetworkInfoService.Instance;
        _startupEngine = startupEngine;

        ResumeLiveCommand = new RelayCommand(ResumeLive);
        ExportJsonCommand = new RelayCommand(async () => await ExportSnapshotAsync(ExportFormat.Json));
        ExportCsvCommand = new RelayCommand(async () => await ExportSnapshotAsync(ExportFormat.Csv));
        SelectMetricCommand = new RelayCommand(param => { if (param is string metric) SelectedMetric = metric; });
        SelectRangeCommand = new RelayCommand(param => { if (param is string range) SelectedRange = range; });

        // Pre-populate 5 items in each list to guarantee stable layout and zero jumping
        for (int i = 0; i < 5; i++)
        {
            TopCpuProcesses.Add(new TopProcessItemViewModel());
            TopRamProcesses.Add(new TopProcessItemViewModel());
            TopGpuProcesses.Add(new TopProcessItemViewModel());
        }

        // Initialize static hardware specs immediately
        InitializeStaticTelemetry();
    }

    private void InitializeStaticTelemetry()
    {
        try
        {
            var storage = _storageService.GetStorageSnapshot();
            UpdateStorageDisplays(storage);

            var net = _networkService.GetPrimaryAdapterInfo();
            UpdateNetworkDisplays(net);

            GpuSubtext = _gpuService.AdapterName;
            IsGpuSupported = _gpuService.IsSupported;
        }
        catch
        {
        }
    }

    public void ApplyMetrics(IReadOnlyList<ProcessSnapshot> processes, SystemMetricsSnapshot system)
    {
        _latestProcesses = processes;
        _latestSystemMetrics = system;
        _lastLiveTimestamp = DateTimeOffset.Now;

        // Ingest into historical rolling store
        _historyStore.AddSample(_lastLiveTimestamp, system, processes);
        HasHistoricalSamples = _historyStore.SampleCount > 1;

        // Sample GPU Hardware Telemetry
        try
        {
            _latestGpuMetrics = _gpuService.CollectSnapshot(processes);
            if (_latestGpuMetrics != null && _latestGpuMetrics.IsSupported)
            {
                GpuPercent = _latestGpuMetrics.UtilizationPercent;
                GpuDisplay = $"{_latestGpuMetrics.UtilizationPercent:F1}%";
                GpuStatusDisplay = $"{_latestGpuMetrics.DedicatedMemoryPercent:F0}% VRAM";
                GpuSubtext = $"{_latestGpuMetrics.AdapterName} • {_latestGpuMetrics.DedicatedMemoryUsedGb:F1}/{_latestGpuMetrics.DedicatedMemoryTotalGb:F1} GB";
                IsGpuSupported = true;
            }
        }
        catch
        {
            IsGpuSupported = false;
        }

        // Rolling Waveforms buffers - seed on first observation so line spans full width immediately
        double netMb = (system.NetworkReceivedBytesPerSec + system.NetworkSentBytesPerSec) / (1024.0 * 1024.0);
        if (_cpuHistory.Count == 0)
        {
            _cpuHistory.Seed(system.CpuPercent);
            _ramHistory.Seed(system.MemoryPercent);
            _gpuHistory.Seed(_latestGpuMetrics?.UtilizationPercent ?? 0.0);
            _diskHistory.Seed(system.ProcessIoBytesPerSec / (1024.0 * 1024.0));
            _netHistory.Seed(netMb);
        }
        else
        {
            _cpuHistory.AddSample(system.CpuPercent);
            _ramHistory.AddSample(system.MemoryPercent);
            _gpuHistory.AddSample(_latestGpuMetrics?.UtilizationPercent ?? 0.0);
            _diskHistory.AddSample(system.ProcessIoBytesPerSec / (1024.0 * 1024.0)); // MB/s
            _netHistory.AddSample(netMb);
        }

        // Update geometries based on currently active chart mode
        UpdateChartDisplay();

        // Hardware & Static Metadata
        ProcessorModel = system.ProcessorName;
        OsVersion = system.OsVersion;
        UptimeDisplay = $"{system.Uptime.Days}d {system.Uptime.Hours:D2}h {system.Uptime.Minutes:D2}m {system.Uptime.Seconds:D2}s";
        InstalledMemoryDisplay = $"{system.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0):F1} GB Physical RAM";

        // Periodic update of Storage and Network adapter details
        try
        {
            var storage = _storageService.GetStorageSnapshot();
            UpdateStorageDisplays(storage);

            var net = _networkService.GetPrimaryAdapterInfo();
            UpdateNetworkDisplays(net);
        }
        catch
        {
        }

        // System Intelligence & Health
        double peakCpu = _cpuHistory.GetPeak();
        double peakRam = _ramHistory.GetPeak();
        var health = SystemHealthEvaluator.Evaluate(
            system,
            _latestGpuMetrics,
            peakCpu,
            peakRam
        );

        HealthStatusText = health.HealthStateDisplay;
        HealthStatusMessage = health.HealthMessage;
        RecentSpikesDisplay = health.RecentSpikesSummary;
        StartupImpactSummary = health.StartupImpactSummary;

        // If currently scrubbing, do not overwrite historical display values
        if (IsScrubbing) return;

        // Update Live Primary Values
        CpuPercent = system.CpuPercent;
        CpuDisplay = $"{system.CpuPercent:F1}%";
        double cpuDelta = Math.Round(system.CpuPercent - _prevCpuPercent, 1);
        CpuTrendDisplay = cpuDelta >= 0 ? $"+{cpuDelta:F1}%" : $"{cpuDelta:F1}%";
        _prevCpuPercent = system.CpuPercent;
        CpuCoresDisplay = $"{system.LogicalCoreCount} cores • {system.ProcessorName}";

        // Sectioned Statistics - CPU Section
        CpuClockMhz = system.CpuClockMhz ?? 3200.0;
        CpuClockDisplay = $"{CpuClockMhz.Value:N0} MHz";
        CpuPowerWatts = system.CpuPowerWatts ?? 35.0;
        CpuPowerDisplay = $"{CpuPowerWatts.Value:F1} W";
        CpuTemperatureC = system.CpuTemperatureC ?? 55.0;
        CpuTemperatureDisplay = $"{CpuTemperatureC.Value:F1} °C";
        CpuTemperatureColor = CpuTemperatureC.Value >= 85.0 ? "#EF4444" : (CpuTemperatureC.Value >= 75.0 ? "#F59E0B" : "#4ADE80");
        CpuUtilizationDisplay = $"{system.CpuPercent:F1}%";
        CpuUtilizationColor = system.CpuPercent >= 90.0 ? "#EF4444" : (system.CpuPercent >= 80.0 ? "#F59E0B" : "#8389FF");

        // Sectioned Statistics - GPU Section
        if (_latestGpuMetrics != null && _latestGpuMetrics.IsSupported)
        {
            GpuClockMhz = _latestGpuMetrics.CoreClockMhz ?? 1425.0;
            GpuClockDisplay = $"{GpuClockMhz.Value:N0} MHz";
            GpuPowerWatts = _latestGpuMetrics.PowerWatts ?? 28.0;
            GpuPowerDisplay = $"{GpuPowerWatts.Value:F1} W";
            GpuTemperatureC = _latestGpuMetrics.TemperatureC ?? 65.0;
            GpuTemperatureDisplay = $"{GpuTemperatureC.Value:F1} °C";
            GpuTemperatureColor = GpuTemperatureC.Value >= 85.0 ? "#EF4444" : (GpuTemperatureC.Value >= 75.0 ? "#F59E0B" : "#2DD4C6");
            GpuVoltageV = _latestGpuMetrics.VoltageV ?? 0.825;
            GpuVoltageDisplay = $"{GpuVoltageV.Value:F3} V";
            VramClockMhz = _latestGpuMetrics.MemoryClockMhz ?? 6001.0;
            VramClockDisplay = $"{VramClockMhz.Value:N0} MHz";
            GpuUtilizationDisplay = $"{_latestGpuMetrics.UtilizationPercent:F1}%";
            GpuUtilizationColor = _latestGpuMetrics.UtilizationPercent >= 90.0 ? "#EF4444" : (_latestGpuMetrics.UtilizationPercent >= 80.0 ? "#F59E0B" : "#8389FF");
            GpuAdapterTitle = _latestGpuMetrics.AdapterName;
        }
        else
        {
            GpuClockMhz = 1425.0;
            GpuClockDisplay = "1,425 MHz";
            GpuPowerWatts = 25.0;
            GpuPowerDisplay = "25.0 W";
            GpuTemperatureC = 55.0;
            GpuTemperatureDisplay = "55.0 °C";
            GpuTemperatureColor = "#2DD4C6";
            GpuVoltageV = 0.825;
            GpuVoltageDisplay = "0.825 V";
            VramClockMhz = 6001.0;
            VramClockDisplay = "6,001 MHz";
            GpuUtilizationDisplay = "0.0%";
            GpuUtilizationColor = "#8389FF";
            GpuAdapterTitle = "DirectX Hardware GPU";
        }

        double usedGb = system.UsedMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = system.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        RamDisplay = $"{usedGb:F1} GB";
        RamDetailDisplay = $"{system.MemoryPercent:F1}% of {totalGb:F1} GB";
        RamPercent = system.MemoryPercent;
        RamTrendDisplay = $"{system.MemoryPercent:F1}%";

        // Disk I/O Rate
        if (system.ProcessIoBytesPerSec > 0)
        {
            DiskIoDisplay = FormatRate(system.ProcessIoBytesPerSec);
            DiskIoDetailDisplay = "Active process read + write";
            DiskIoProgress = Math.Clamp(system.ProcessIoBytesPerSec / (50.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);
        }
        else
        {
            DiskIoDisplay = "0.0 KB/s";
            DiskIoDetailDisplay = "No active disk I/O";
            DiskIoProgress = 0.0;
        }

        // Network Rate
        double totalNet = system.NetworkReceivedBytesPerSec + system.NetworkSentBytesPerSec;
        NetworkDisplay = FormatRate(totalNet);
        NetworkDetailDisplay = $"In: {FormatRate(system.NetworkReceivedBytesPerSec)} • Out: {FormatRate(system.NetworkSentBytesPerSec)}";
        NetworkDownloadDisplay = $"{FormatRate(system.NetworkReceivedBytesPerSec)} In";
        NetworkUploadDisplay = $"{FormatRate(system.NetworkSentBytesPerSec)} Out";
        NetworkProgress = Math.Clamp(totalNet / (10.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);

        ProcessCountDisplay = $"{system.ProcessCount}";
        ProcessCountTrend = system.ProcessCount > 400 ? "Elevated" : "Normal";
        ProcessSubtext = $"{system.ProcessCount} active kernel processes";
        ProcessCountProgress = Math.Clamp((system.ProcessCount / 500.0) * 100.0, 5.0, 100.0);
        StatusPillText = $"System online • {system.ProcessCount} processes";

        // Update timeline range bounds
        var history = _historyStore.GetHistory();
        if (history.Count > 0)
        {
            var span = _lastLiveTimestamp - history[0].Timestamp;
            TimelineWindowDisplay = span.TotalMinutes >= 1
                ? $"-{Math.Floor(span.TotalMinutes):F0}m {span.Seconds:D2}s"
                : $"-{span.Seconds:D2}s";
        }
        TimelineTimeDisplay = "Live • Streaming";
        _scrubberValue = 1.0;
        OnPropertyChanged(nameof(ScrubberValue));

        // Update Top Resource Consumers (Top 5 each)
        UpdateTopOffenders(processes, _latestGpuMetrics);
    }

    private void UpdateStorageDisplays(StorageMetricsSnapshot storage)
    {
        if (storage.PrimaryDrive != null)
        {
            PrimaryDriveName = storage.PrimaryDrive.Name;
            PrimaryDriveLabel = $"{storage.PrimaryDrive.Label} ({storage.PrimaryDrive.FreeGb:F1} GB free)";
            PrimaryDriveCapacityDisplay = $"{storage.PrimaryDrive.UsedGb:F1} GB / {storage.PrimaryDrive.TotalGb:F1} GB";
            PrimaryDrivePercent = storage.PrimaryDrive.PercentUsed;
        }
        StoragePartitionsSummary = $"{storage.AllDrives.Count} active fixed drives • {storage.TotalCapacityGb:F0} GB total capacity";
    }

    private void UpdateNetworkDisplays(NetworkAdapterSnapshot net)
    {
        NetworkAdapterName = net.Name;
        NetworkAdapterDescription = net.Description;
        NetworkInterfaceType = net.InterfaceTypeDisplay;
        NetworkLinkSpeed = net.LinkSpeedDisplay;
    }

    private void UpdateChartDisplay()
    {
        double width = _cardViewportWidth > 50 ? _cardViewportWidth : 320.0;
        double height = _cardViewportHeight > 20 ? _cardViewportHeight : 95.0;

        // 1. CPU Waveform & Area
        CpuWaveformPoints = _cpuHistory.GeneratePoints(width, height, 100.0);
        CpuCurveGeometry = _cpuHistory.GenerateSmoothCurveGeometry(width, height, 100.0);
        CpuAreaGeometry = _cpuHistory.GenerateSmoothAreaGeometry(width, height, 100.0);

        // 2. RAM Waveform & Area
        RamWaveformPoints = _ramHistory.GeneratePoints(width, height, 100.0);
        RamCurveGeometry = _ramHistory.GenerateSmoothCurveGeometry(width, height, 100.0);
        RamAreaGeometry = _ramHistory.GenerateSmoothAreaGeometry(width, height, 100.0);

        // 3. GPU Waveform & Area
        GpuCurveGeometry = _gpuHistory.GenerateSmoothCurveGeometry(width, height, 100.0);
        GpuAreaGeometry = _gpuHistory.GenerateSmoothAreaGeometry(width, height, 100.0);

        // 4. Disk Throughput Waveform & Area (Dynamic Scale)
        double maxDisk = Math.Max(10.0, _diskHistory.GetPeak() * 1.2);
        DiskCurveGeometry = _diskHistory.GenerateSmoothCurveGeometry(width, height, maxDisk);
        DiskAreaGeometry = _diskHistory.GenerateSmoothAreaGeometry(width, height, maxDisk);

        // 5. Network Activity Waveform & Area (Dynamic Scale)
        double maxNet = Math.Max(2.0, _netHistory.GetPeak() * 1.2);
        NetCurveGeometry = _netHistory.GenerateSmoothCurveGeometry(width, height, maxNet);
        NetAreaGeometry = _netHistory.GenerateSmoothAreaGeometry(width, height, maxNet);

        // Primary / Secondary alias for Overview Dual Stream
        PrimaryCurveGeometry = CpuCurveGeometry;
        PrimaryAreaGeometry = CpuAreaGeometry;
        SecondaryCurveGeometry = RamCurveGeometry;
        SecondaryAreaGeometry = RamAreaGeometry;
        IsDualMetric = true;

        // Card header metrics
        CpuPeakDisplay = $"{_cpuHistory.GetPeak():F1}%";
        RamPercentDisplay = $"{_ramPercent:F1}%";
        DiskPeakDisplay = $"{_diskHistory.GetPeak():F1} MB/s";
        NetPeakDisplay = $"{_netHistory.GetPeak():F2} MB/s";

        double cpuLiveY = PerformanceHistoryBuffer.CalculateY(_cpuHistory.GetLatest(), height, 100.0);
        double ramLiveY = PerformanceHistoryBuffer.CalculateY(_ramHistory.GetLatest(), height, 100.0);
        PrimaryLiveY = cpuLiveY;
        SecondaryLiveY = ramLiveY;

        if (SelectedMetric == "Overview")
        {
            IsDualMetric = true;
            IsOverviewChannel = true;
            CurrentMetricDisplay = "Overview • CPU & Memory Telemetry";
            ActiveChannelName = "System Telemetry";
            ActiveChannelTag = "System Telemetry";
            ActiveChannelValue = CpuDisplay;
            ActiveChannelSubtext = $"CPU: {CpuDisplay} • RAM: {RamDisplay} ({RamPercent:F0}%)";
            ActiveChannelPeak = $"{_cpuHistory.GetPeak():F1}%";
            ActiveChannelAverage = $"{_cpuHistory.GetAverage():F1}%";
            ActiveChannelMin = $"{_cpuHistory.GetMinimum():F1}%";
            ActiveChannelStatus = "60s Window • 3s Buffer Delay";
            ActiveChannelColor = "#8389FF";
        }
        else if (SelectedMetric == "Cpu")
        {
            IsDualMetric = false;
            IsOverviewChannel = false;
            CurrentMetricDisplay = $"CPU Utilization • {CpuDisplay}";
            ActiveChannelName = "CPU Utilization";
            ActiveChannelTag = "Processor Array";
            ActiveChannelValue = CpuDisplay;
            ActiveChannelSubtext = ProcessorModel;
            ActiveChannelPeak = $"{_cpuHistory.GetPeak():F1}%";
            ActiveChannelAverage = $"{_cpuHistory.GetAverage():F1}%";
            ActiveChannelMin = $"{_cpuHistory.GetMinimum():F1}%";
            ActiveChannelStatus = _cpuHistory.GetPeak() > 80.0 ? "High Workload Detected" : "Optimal Processing";
            ActiveChannelColor = "#8389FF";
        }
        else if (SelectedMetric == "Memory")
        {
            IsDualMetric = false;
            IsOverviewChannel = false;
            CurrentMetricDisplay = $"Physical Memory • {RamDisplay} ({RamPercent:F1}%)";
            ActiveChannelName = "Memory Allocation";
            ActiveChannelTag = "Physical Memory";
            ActiveChannelValue = RamDisplay;
            ActiveChannelSubtext = RamDetailDisplay;
            ActiveChannelPeak = $"{_ramHistory.GetPeak():F1}%";
            ActiveChannelAverage = $"{_ramHistory.GetAverage():F1}%";
            ActiveChannelMin = $"{_ramHistory.GetMinimum():F1}%";
            ActiveChannelStatus = "Stable Memory Footprint";
            ActiveChannelColor = "#2DD4C6";
        }
        else if (SelectedMetric == "Gpu")
        {
            IsDualMetric = false;
            IsOverviewChannel = false;
            CurrentMetricDisplay = $"GPU 3D Engine • {GpuDisplay}";
            ActiveChannelName = "GPU 3D Engine";
            ActiveChannelTag = "Graphics Pipeline";
            ActiveChannelValue = GpuDisplay;
            ActiveChannelSubtext = GpuSubtext;
            ActiveChannelPeak = $"{_gpuHistory.GetPeak():F1}%";
            ActiveChannelAverage = $"{_gpuHistory.GetAverage():F1}%";
            ActiveChannelMin = $"{_gpuHistory.GetMinimum():F1}%";
            ActiveChannelStatus = IsGpuSupported ? "DirectX Accelerated" : "Integrated / Software";
            ActiveChannelColor = "#8389FF";
        }
        else if (SelectedMetric == "Disk")
        {
            IsDualMetric = false;
            IsOverviewChannel = false;
            CurrentMetricDisplay = $"Disk Throughput • {DiskIoDisplay}";
            ActiveChannelName = "Disk Throughput";
            ActiveChannelTag = "Storage I/O";
            ActiveChannelValue = DiskIoDisplay;
            ActiveChannelSubtext = DiskIoDetailDisplay;
            ActiveChannelPeak = $"{_diskHistory.GetPeak():F1} MB/s";
            ActiveChannelAverage = $"{_diskHistory.GetAverage():F1} MB/s";
            ActiveChannelMin = $"{_diskHistory.GetMinimum():F1} MB/s";
            ActiveChannelStatus = "Storage Controller Active";
            ActiveChannelColor = "#F0A94E";
        }
        else if (SelectedMetric == "Network")
        {
            IsDualMetric = false;
            IsOverviewChannel = false;
            CurrentMetricDisplay = $"Network Traffic • {NetworkDisplay}";
            ActiveChannelName = "Network Throughput";
            ActiveChannelTag = "Network Interface";
            ActiveChannelValue = NetworkDisplay;
            ActiveChannelSubtext = NetworkDetailDisplay;
            ActiveChannelPeak = $"{_netHistory.GetPeak():F2} MB/s";
            ActiveChannelAverage = $"{_netHistory.GetAverage():F2} MB/s";
            ActiveChannelMin = $"{_netHistory.GetMinimum():F2} MB/s";
            ActiveChannelStatus = $"{NetworkAdapterName} • {NetworkLinkSpeed}";
            ActiveChannelColor = "#F2777C";
        }
    }

    public void ApplyScrubbedSample(double fraction)
    {
        IsScrubbing = true;
        var sample = _historyStore.GetSampleByFraction(fraction);
        if (sample == null) return;

        CpuPercent = sample.CpuPercent;
        CpuDisplay = $"{sample.CpuPercent:F1}%";
        CpuCoresDisplay = $"Historical • {sample.Timestamp:HH:mm:ss}";

        double usedGb = sample.UsedMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = sample.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        RamDisplay = $"{usedGb:F1} GB";
        RamDetailDisplay = $"{sample.MemoryPercent:F1}% of {totalGb:F1} GB";
        RamPercent = sample.MemoryPercent;
        RamTrendDisplay = $"{sample.MemoryPercent:F1}%";

        if (sample.ProcessIoBytesPerSec > 0)
        {
            DiskIoDisplay = FormatRate(sample.ProcessIoBytesPerSec);
            DiskIoDetailDisplay = "Historical process read + write";
            DiskIoProgress = Math.Clamp(sample.ProcessIoBytesPerSec / (50.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);
        }
        else
        {
            DiskIoDisplay = "0.0 KB/s";
            DiskIoDetailDisplay = "No active disk I/O";
            DiskIoProgress = 0.0;
        }

        double totalNet = sample.NetworkReceivedBytesPerSec + sample.NetworkSentBytesPerSec;
        NetworkDisplay = FormatRate(totalNet);
        NetworkDetailDisplay = $"In: {FormatRate(sample.NetworkReceivedBytesPerSec)} • Out: {FormatRate(sample.NetworkSentBytesPerSec)}";
        NetworkDownloadDisplay = $"{FormatRate(sample.NetworkReceivedBytesPerSec)} In";
        NetworkUploadDisplay = $"{FormatRate(sample.NetworkSentBytesPerSec)} Out";
        NetworkProgress = Math.Clamp(totalNet / (10.0 * 1024.0 * 1024.0) * 100.0, 2.0, 100.0);

        ProcessCountDisplay = $"{sample.ProcessCount}";
        ProcessCountProgress = Math.Clamp((sample.ProcessCount / 500.0) * 100.0, 5.0, 100.0);

        var offset = _lastLiveTimestamp - sample.Timestamp;
        string offsetStr = offset.TotalMinutes >= 1
            ? $"-{Math.Floor(offset.TotalMinutes):F0}m {offset.Seconds:D2}s"
            : $"-{offset.Seconds:D2}s";

        TimelineTimeDisplay = $"Scrubbing: {offsetStr} ({sample.Timestamp:HH:mm:ss})";
        StatusPillText = $"Historical • {sample.Timestamp:HH:mm:ss}";

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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(dir);
            targetPath = Path.Combine(dir, defaultFileName);
        }

        try
        {
            var heuristicMap = _heuristicEngine != null
                ? await Task.Run(() => _heuristicEngine.Evaluate(_latestProcesses))
                : null;

            await _exportService.ExportToFileAsync(
                targetPath,
                format,
                _latestSystemMetrics,
                _latestProcesses,
                heuristicMap
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

        var topCpu = topProcesses.OrderByDescending(p => p.CpuPercent).Take(5).ToList();
        for (int i = 0; i < 5; i++)
        {
            if (i < topCpu.Count)
            {
                var p = topCpu[i];
                TopCpuProcesses[i].Name = p.Name;
                TopCpuProcesses[i].Pid = p.Pid;
                TopCpuProcesses[i].Subtext = p.FlagReason != null ? $"PID {p.Pid} • {p.FlagReason}" : $"PID {p.Pid}";
                TopCpuProcesses[i].ValueDisplay = $"{p.CpuPercent:F2}%";
                TopCpuProcesses[i].PercentValue = Math.Clamp(p.CpuPercent, 1.0, 100.0);
            }
        }

        var topRam = topProcesses.OrderByDescending(p => p.RamBytes).Take(5).ToList();
        for (int i = 0; i < 5; i++)
        {
            if (i < topRam.Count)
            {
                var p = topRam[i];
                TopRamProcesses[i].Name = p.Name;
                TopRamProcesses[i].Pid = p.Pid;
                TopRamProcesses[i].Subtext = $"PID {p.Pid}";
                double mb = p.RamBytes / (1024.0 * 1024.0);
                TopRamProcesses[i].ValueDisplay = mb >= 1024.0 ? $"{mb / 1024.0:F2} GB" : $"{mb:F0} MB";
                TopRamProcesses[i].PercentValue = Math.Clamp((mb / 4096.0) * 100.0, 1.0, 100.0);
            }
        }
    }

    private void UpdateTopOffenders(IReadOnlyList<ProcessSnapshot> processes, GpuMetricsSnapshot? gpuMetrics)
    {
        if (processes == null || processes.Count == 0) return;

        // Top 5 CPU
        var topCpu = processes
            .Where(p => p.Pid != 0 && !p.IsAccessDenied)
            .OrderByDescending(p => p.CpuPercent)
            .Take(5)
            .ToList();

        for (int i = 0; i < 5; i++)
        {
            if (i < topCpu.Count)
            {
                var p = topCpu[i];
                TopCpuProcesses[i].Name = p.Name;
                TopCpuProcesses[i].Pid = p.Pid;
                TopCpuProcesses[i].Subtext = $"PID {p.Pid}";
                TopCpuProcesses[i].ValueDisplay = $"{p.CpuPercent:F2}%";
                TopCpuProcesses[i].PercentValue = Math.Clamp(p.CpuPercent, 1.0, 100.0);
            }
        }

        // Top 5 RAM
        var topRam = processes
            .Where(p => p.Pid != 0)
            .OrderByDescending(p => p.RamBytes)
            .Take(5)
            .ToList();

        for (int i = 0; i < 5; i++)
        {
            if (i < topRam.Count)
            {
                var p = topRam[i];
                TopRamProcesses[i].Name = p.Name;
                TopRamProcesses[i].Pid = p.Pid;
                TopRamProcesses[i].Subtext = $"PID {p.Pid}";
                double mb = p.RamBytes / (1024.0 * 1024.0);
                TopRamProcesses[i].ValueDisplay = mb >= 1024.0 ? $"{mb / 1024.0:F2} GB" : $"{mb:F0} MB";
                TopRamProcesses[i].PercentValue = Math.Clamp((mb / 4096.0) * 100.0, 1.0, 100.0);
            }
        }

        // Top 5 GPU
        if (gpuMetrics != null && gpuMetrics.TopGpuProcesses != null && gpuMetrics.TopGpuProcesses.Count > 0)
        {
            for (int i = 0; i < 5; i++)
            {
                if (i < gpuMetrics.TopGpuProcesses.Count)
                {
                    var g = gpuMetrics.TopGpuProcesses[i];
                    TopGpuProcesses[i].Name = g.Name;
                    TopGpuProcesses[i].Pid = g.Pid;
                    TopGpuProcesses[i].Subtext = $"PID {g.Pid} • 3D Engine";
                    TopGpuProcesses[i].ValueDisplay = $"{g.GpuPercent:F1}%";
                    TopGpuProcesses[i].PercentValue = Math.Clamp(g.GpuPercent, 1.0, 100.0);
                }
                else
                {
                    TopGpuProcesses[i].Name = "—";
                    TopGpuProcesses[i].Pid = 0;
                    TopGpuProcesses[i].Subtext = "Idle";
                    TopGpuProcesses[i].ValueDisplay = "0.0%";
                    TopGpuProcesses[i].PercentValue = 0.0;
                }
            }
        }
        else
        {
            // Fallback for GPU processes
            for (int i = 0; i < 5; i++)
            {
                TopGpuProcesses[i].Name = i == 0 && (gpuMetrics == null || !gpuMetrics.IsSupported) ? "GPU process metrics unavailable" : "—";
                TopGpuProcesses[i].Pid = 0;
                TopGpuProcesses[i].Subtext = i == 0 ? "Hardware counter not present" : "Idle";
                TopGpuProcesses[i].ValueDisplay = "0.0%";
                TopGpuProcesses[i].PercentValue = 0.0;
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

    private bool _isDisposed;
    public int DisposeCount { get; private set; }

    public void Dispose()
    {
        DisposeCount++;
        if (_isDisposed) return;
        _isDisposed = true;

        _cpuHistory.Clear();
        _ramHistory.Clear();
        _gpuHistory.Clear();
        _diskHistory.Clear();
        _netHistory.Clear();

        void SafeClear<T>(System.Collections.ObjectModel.ObservableCollection<T> collection)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                d.Invoke(collection.Clear);
            }
            else
            {
                collection.Clear();
            }
        }

        SafeClear(TopCpuProcesses);
        SafeClear(TopRamProcesses);
        SafeClear(TopGpuProcesses);
    }
}
