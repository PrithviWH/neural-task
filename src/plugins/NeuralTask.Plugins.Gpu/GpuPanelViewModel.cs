using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// Selectable GPU adapter tab/pill ViewModel.
/// </summary>
public class GpuDeviceItemViewModel
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public long DedicatedBytes { get; init; }
    public string DriverVersion { get; init; } = string.Empty;
    public bool IsDiscrete { get; init; }
    public bool IsSelected { get; set; }

    public string BadgeText => IsDiscrete ? "dGPU" : "iGPU";
    public string PillBackground => IsSelected ? "#26234B" : "Transparent";
    public string PillBorderBrush => IsSelected ? "#8389FF" : "#262A34";
    public string PillForeground => IsSelected ? "#8389FF" : "#949AA8";
}

/// <summary>
/// Presentation ViewModel for an individual process utilizing GPU hardware acceleration.
/// </summary>
public class GpuProcessItemViewModel
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Engine { get; init; } = "3D Raster";
    public double UtilizationPercent { get; init; }
    public string UtilizationDisplay => UtilizationPercent > 0.05 ? $"{UtilizationPercent:F1}%" : "< 0.1%";
    public string Mode { get; init; } = "Hardware Accelerated Surface";
    public string MemoryDisplay { get; init; } = "—";

    public string EngineBadgeBackground => Engine switch
    {
        "3D Raster" => "#26234B",
        "Compute / DirectML" => "#2E1F4D",
        "Video Encode" => "#3B1828",
        "Video Decode" => "#123838",
        "DMA / Copy" => "#3B2D18",
        _ => "#1F2937"
    };

    public string EngineBadgeForeground => Engine switch
    {
        "3D Raster" => "#8389FF",
        "Compute / DirectML" => "#C084FC",
        "Video Encode" => "#EC4899",
        "Video Decode" => "#2DD4C6",
        "DMA / Copy" => "#F59E0B",
        _ => "#9CA3AF"
    };

    public string EngineIconGeometry => Engine switch
    {
        "3D Raster" => "M 12,2 L 2,7 L 12,12 L 22,7 Z M 2,17 L 12,22 L 22,17 M 2,12 L 12,17 L 22,12",
        "Compute / DirectML" => "M 12,2 L 12,6 M 12,18 L 12,22 M 2,12 L 6,12 M 18,12 L 22,12 M 7,7 L 17,7 L 17,17 L 7,17 Z",
        "Video Encode" => "M 4,4 L 20,4 L 20,20 L 4,20 Z M 15,12 L 9,8 L 9,16 Z",
        "Video Decode" => "M 4,4 L 20,4 L 20,20 L 4,20 Z M 10,8 L 16,12 L 10,16 Z",
        "DMA / Copy" => "M 4,8 L 16,8 M 12,4 L 16,8 L 12,12 M 20,16 L 8,16 M 12,12 L 8,16 L 12,20",
        _ => "M 4,4 L 20,4 L 20,20 L 4,20 Z"
    };
}

/// <summary>
/// Presentation ViewModel for the elevated GPU monitoring operations center.
/// Follows rule of thumb: "Is it working hard, and is it running hot/full" first.
/// Prioritizes discrete NVIDIA RTX 3060 6 GB GPU.
/// </summary>
public class GpuPanelViewModel : INotifyPropertyChanged
{
    private readonly LinkedList<double> _history3D = new();
    private readonly LinkedList<double> _historyVram = new();
    private const int MaxHistoryPoints = 60;

    // Thin Context Line
    private string _adapterName = "NVIDIA GeForce RTX 3060 Laptop GPU";
    private string _driverVersion = "v32.0.16.2124";
    private string _videoProcessor = "NVIDIA GeForce RTX 3060 Laptop GPU (GA106)";
    private string _displayResolution = "1920 x 1080 @ 144Hz";
    private string _directXLevel = "Direct3D 12.2 • Feature Level 12_2 (DirectX 12 Ultimate)";
    private string _hardwareStatus = "Direct3D Hardware Acceleration OK";
    private string _statusText = "D3D12 // NVIDIA GEFORCE RTX 3060";

    // 1. KPI: Utilization %
    private double _utilizationPercent;
    private double _peakUtilizationPercent;
    private double _averageUtilizationPercent;
    private string _miniSparklineGeometry = "M 0,20 L 100,20";

    // 2. KPI: VRAM Allocation (Default 6.0 GB for RTX 3060)
    private double _dedicatedMemoryUsedMb = 446.0;
    private double _dedicatedMemoryTotalMb = 6144.0;
    private double _dedicatedMemoryUsedGb = 0.4;
    private double _dedicatedMemoryTotalGb = 6.0;
    private double _dedicatedMemoryPercent = 7.3;
    private double _availableMemoryMb = 5698.0;
    private double _availableMemoryGb = 5.6;
    private double _sharedMemoryUsedMb = 296.0;

    // 3. KPI: Temperature (color-coded by threshold: green/amber/red)
    private double _temperatureC = 78.0;
    private double _hotspotTemperatureC = 82.5;

    // 4. KPI: Power Draw (W)
    private double _powerWatts = 31.0;
    private double _powerTargetWatts = 80.0;

    // Mid Section: Per-Engine Breakdown
    private double _engine3DPercent;
    private double _engineComputePercent;
    private double _engineVideoEncodePercent;
    private double _engineVideoDecodePercent;
    private double _engineCopyPercent;

    // Mid Section: Clock Speeds & Throttling
    private int _coreClockMhz = 1425;
    private int _boostClockMhz = 2100;
    private int _memoryClockMhz = 7151;
    private int _memoryClockMaxMhz = 7500;
    private string _throttleStatus = "Optimal • P-State P0";

    // Below Fold: Cooling & Fans
    private int _fanSpeedRpm = 1450;
    private double _fanSpeedPercent = 48.0;
    private string _fanProfile = "Active Dual Fans (48% PWM • Quiet Profile)";

    // Live Historical Graph
    private string _timelineLineGeometry = "M 0,120 L 600,120";
    private string _timelineAreaGeometry = "M 0,120 L 0,120 L 600,120 L 600,120 Z";
    private string _timelineVramGeometry = "M 0,120 L 600,120";
    private string _sparklineGeometry = "M 0,60 L 300,60";

    public ObservableCollection<GpuProcessItemViewModel> TopProcesses { get; } = new();
    public ObservableCollection<GpuDeviceItemViewModel> AvailableDevices { get; } = new();

    #region Context Line Properties
    public string AdapterName
    {
        get => _adapterName;
        set => SetProperty(ref _adapterName, value);
    }

    public string DriverVersion
    {
        get => _driverVersion;
        set => SetProperty(ref _driverVersion, value);
    }

    public string VideoProcessor
    {
        get => _videoProcessor;
        set => SetProperty(ref _videoProcessor, value);
    }

    public string DisplayResolution
    {
        get => _displayResolution;
        set => SetProperty(ref _displayResolution, value);
    }

    public string DirectXLevel
    {
        get => _directXLevel;
        set => SetProperty(ref _directXLevel, value);
    }

    public string HardwareStatus
    {
        get => _hardwareStatus;
        set => SetProperty(ref _hardwareStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
    #endregion

    #region KPI 1: Utilization %
    public double UtilizationPercent
    {
        get => _utilizationPercent;
        set
        {
            if (SetProperty(ref _utilizationPercent, value))
            {
                OnPropertyChanged(nameof(UtilizationDisplay));
            }
        }
    }

    public string UtilizationDisplay => $"{_utilizationPercent:F1}%";

    public double PeakUtilizationPercent
    {
        get => _peakUtilizationPercent;
        set => SetProperty(ref _peakUtilizationPercent, value);
    }

    public double AverageUtilizationPercent
    {
        get => _averageUtilizationPercent;
        set => SetProperty(ref _averageUtilizationPercent, value);
    }

    public string MiniSparklineGeometry
    {
        get => _miniSparklineGeometry;
        set => SetProperty(ref _miniSparklineGeometry, value);
    }
    #endregion

    #region KPI 2: VRAM Allocation
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

    public double DedicatedMemoryUsedGb
    {
        get => _dedicatedMemoryUsedGb;
        set
        {
            if (SetProperty(ref _dedicatedMemoryUsedGb, value))
            {
                OnPropertyChanged(nameof(DedicatedVramDisplay));
            }
        }
    }

    public double DedicatedMemoryTotalGb
    {
        get => _dedicatedMemoryTotalGb;
        set
        {
            if (SetProperty(ref _dedicatedMemoryTotalGb, value))
            {
                OnPropertyChanged(nameof(DedicatedVramDisplay));
            }
        }
    }

    public string DedicatedVramDisplay => $"{_dedicatedMemoryUsedGb:F1} / {_dedicatedMemoryTotalGb:F1} GB";

    public double DedicatedMemoryPercent
    {
        get => _dedicatedMemoryPercent;
        set => SetProperty(ref _dedicatedMemoryPercent, value);
    }

    public double AvailableMemoryMb
    {
        get => _availableMemoryMb;
        set => SetProperty(ref _availableMemoryMb, value);
    }

    public double AvailableMemoryGb
    {
        get => _availableMemoryGb;
        set => SetProperty(ref _availableMemoryGb, value);
    }

    public double SharedMemoryUsedMb
    {
        get => _sharedMemoryUsedMb;
        set => SetProperty(ref _sharedMemoryUsedMb, value);
    }
    #endregion

    #region KPI 3: Temperature (color-coded by threshold: green/amber/red)
    public double TemperatureC
    {
        get => _temperatureC;
        set
        {
            if (SetProperty(ref _temperatureC, value))
            {
                OnPropertyChanged(nameof(TemperatureDisplay));
                OnPropertyChanged(nameof(TemperatureColor));
                OnPropertyChanged(nameof(TemperatureStatus));
                OnPropertyChanged(nameof(TemperatureBadgeBackground));
                OnPropertyChanged(nameof(TemperatureBadgeForeground));
            }
        }
    }

    public string TemperatureDisplay => $"{_temperatureC:F0}°C";

    public double HotspotTemperatureC
    {
        get => _hotspotTemperatureC;
        set => SetProperty(ref _hotspotTemperatureC, value);
    }

    public string TemperatureColor => _temperatureC switch
    {
        < 65.0 => "#2DD4C6",  // Clarity Teal: Nominal / Optimal
        < 82.0 => "#F0A94E",  // Clarity Muted Amber: Warm / Heavy Compute (78-81°C NVIDIA normal high load)
        _ => "#F2777C"       // Clarity Muted Rose: Thermal Throttling / Critical (> 82°C)
    };

    public string TemperatureStatus => _temperatureC switch
    {
        < 65.0 => "Optimal",
        < 82.0 => "Warm",
        _ => "Throttling"
    };

    public string TemperatureBadgeBackground => _temperatureC switch
    {
        < 65.0 => "#1A3338",
        < 82.0 => "#382C18",
        _ => "#3B1E24"
    };

    public string TemperatureBadgeForeground => TemperatureColor;
    #endregion

    #region KPI 4: Power Draw (W)
    public double PowerWatts
    {
        get => _powerWatts;
        set
        {
            if (SetProperty(ref _powerWatts, value))
            {
                OnPropertyChanged(nameof(PowerDisplay));
                OnPropertyChanged(nameof(PowerPercent));
            }
        }
    }

    public string PowerDisplay => $"{_powerWatts:F0} W";

    public double PowerTargetWatts
    {
        get => _powerTargetWatts;
        set
        {
            if (SetProperty(ref _powerTargetWatts, value))
            {
                OnPropertyChanged(nameof(PowerPercent));
            }
        }
    }

    public double PowerPercent => _powerTargetWatts > 0 ? Math.Clamp((_powerWatts / _powerTargetWatts) * 100.0, 0.0, 100.0) : 0.0;
    #endregion

    #region Mid Section: Per-Engine Breakdown (5 engines)
    public double Engine3DPercent
    {
        get => _engine3DPercent;
        set
        {
            if (SetProperty(ref _engine3DPercent, value))
            {
                OnPropertyChanged(nameof(Engine3DDisplay));
                OnPropertyChanged(nameof(Engine3DStatus));
            }
        }
    }
    public string Engine3DDisplay => $"{_engine3DPercent:F1}%";
    public string Engine3DStatus => _engine3DPercent > 1.0 ? "Rendering" : (_engine3DPercent > 0.05 ? "Active" : "Nominal");

    public double EngineComputePercent
    {
        get => _engineComputePercent;
        set
        {
            if (SetProperty(ref _engineComputePercent, value))
            {
                OnPropertyChanged(nameof(EngineComputeDisplay));
                OnPropertyChanged(nameof(EngineComputeStatus));
            }
        }
    }
    public string EngineComputeDisplay => $"{_engineComputePercent:F1}%";
    public string EngineComputeStatus => _engineComputePercent > 0.5 ? "Executing Kernel" : "Idle";

    public double EngineVideoEncodePercent
    {
        get => _engineVideoEncodePercent;
        set
        {
            if (SetProperty(ref _engineVideoEncodePercent, value))
            {
                OnPropertyChanged(nameof(EngineVideoEncodeDisplay));
                OnPropertyChanged(nameof(EngineVideoEncodeStatus));
            }
        }
    }
    public string EngineVideoEncodeDisplay => $"{_engineVideoEncodePercent:F1}%";
    public string EngineVideoEncodeStatus => _engineVideoEncodePercent > 0.5 ? "Encoding Stream" : "Idle";

    public double EngineVideoDecodePercent
    {
        get => _engineVideoDecodePercent;
        set
        {
            if (SetProperty(ref _engineVideoDecodePercent, value))
            {
                OnPropertyChanged(nameof(EngineVideoDecodeDisplay));
                OnPropertyChanged(nameof(EngineVideoDecodeStatus));
            }
        }
    }
    public string EngineVideoDecodeDisplay => $"{_engineVideoDecodePercent:F1}%";
    public string EngineVideoDecodeStatus => _engineVideoDecodePercent > 0.5 ? "Decoding Stream" : "Idle";

    public double EngineCopyPercent
    {
        get => _engineCopyPercent;
        set
        {
            if (SetProperty(ref _engineCopyPercent, value))
            {
                OnPropertyChanged(nameof(EngineCopyDisplay));
                OnPropertyChanged(nameof(EngineCopyStatus));
            }
        }
    }
    public string EngineCopyDisplay => $"{_engineCopyPercent:F1}%";
    public string EngineCopyStatus => _engineCopyPercent > 0.5 ? "Transferring" : "Idle";
    #endregion

    #region Mid Section: Clock Speeds & Throttling
    public int CoreClockMhz
    {
        get => _coreClockMhz;
        set
        {
            if (SetProperty(ref _coreClockMhz, value))
            {
                OnPropertyChanged(nameof(CoreClockDisplay));
                OnPropertyChanged(nameof(CoreClockPercent));
            }
        }
    }
    public string CoreClockDisplay => $"{_coreClockMhz:N0} MHz";

    public int BoostClockMhz
    {
        get => _boostClockMhz;
        set
        {
            if (SetProperty(ref _boostClockMhz, value))
            {
                OnPropertyChanged(nameof(CoreClockPercent));
            }
        }
    }
    public double CoreClockPercent => _boostClockMhz > 0 ? Math.Clamp(((double)_coreClockMhz / _boostClockMhz) * 100.0, 0.0, 100.0) : 0.0;

    public int MemoryClockMhz
    {
        get => _memoryClockMhz;
        set
        {
            if (SetProperty(ref _memoryClockMhz, value))
            {
                OnPropertyChanged(nameof(MemoryClockDisplay));
                OnPropertyChanged(nameof(MemoryClockPercent));
            }
        }
    }
    public string MemoryClockDisplay => $"{_memoryClockMhz:N0} MHz";

    public int MemoryClockMaxMhz
    {
        get => _memoryClockMaxMhz;
        set
        {
            if (SetProperty(ref _memoryClockMaxMhz, value))
            {
                OnPropertyChanged(nameof(MemoryClockPercent));
            }
        }
    }
    public double MemoryClockPercent => _memoryClockMaxMhz > 0 ? Math.Clamp(((double)_memoryClockMhz / _memoryClockMaxMhz) * 100.0, 0.0, 100.0) : 0.0;

    public string ThrottleStatus
    {
        get => _throttleStatus;
        set => SetProperty(ref _throttleStatus, value);
    }
    #endregion

    #region Below Fold: Cooling & Fans
    public int FanSpeedRpm
    {
        get => _fanSpeedRpm;
        set
        {
            if (SetProperty(ref _fanSpeedRpm, value))
            {
                OnPropertyChanged(nameof(FanRpmDisplay));
            }
        }
    }
    public string FanRpmDisplay => _fanSpeedRpm == 0 ? "0 RPM (Silent)" : $"{_fanSpeedRpm:N0} RPM";

    public double FanSpeedPercent
    {
        get => _fanSpeedPercent;
        set => SetProperty(ref _fanSpeedPercent, value);
    }

    public string FanProfile
    {
        get => _fanProfile;
        set => SetProperty(ref _fanProfile, value);
    }
    #endregion

    #region Live Historical Graph
    public string TimelineLineGeometry
    {
        get => _timelineLineGeometry;
        set => SetProperty(ref _timelineLineGeometry, value);
    }

    public string TimelineAreaGeometry
    {
        get => _timelineAreaGeometry;
        set => SetProperty(ref _timelineAreaGeometry, value);
    }

    public string TimelineVramGeometry
    {
        get => _timelineVramGeometry;
        set => SetProperty(ref _timelineVramGeometry, value);
    }

    public string SparklineGeometry
    {
        get => _sparklineGeometry;
        set => SetProperty(ref _sparklineGeometry, value);
    }
    #endregion

    public GpuPanelViewModel()
    {
        for (int i = 0; i < MaxHistoryPoints; i++)
        {
            _history3D.AddLast(0.0);
            _historyVram.AddLast(7.3);
        }
        UpdateTimelineGeometries();
    }

    public void ApplyMetrics(GpuMetrics metrics)
    {
        AdapterName = metrics.AdapterName;
        DriverVersion = metrics.DriverVersion;
        VideoProcessor = metrics.VideoProcessor;
        DisplayResolution = metrics.DisplayResolution;
        DirectXLevel = metrics.DirectXLevel;
        HardwareStatus = metrics.Status;

        UtilizationPercent = metrics.UtilizationPercent;
        DedicatedMemoryUsedMb = metrics.DedicatedMemoryUsedMb;
        DedicatedMemoryTotalMb = metrics.DedicatedMemoryTotalMb;
        DedicatedMemoryUsedGb = metrics.DedicatedMemoryUsedGb;
        DedicatedMemoryTotalGb = metrics.DedicatedMemoryTotalGb;
        DedicatedMemoryPercent = metrics.DedicatedMemoryPercent;
        AvailableMemoryMb = metrics.AvailableMemoryMb;
        AvailableMemoryGb = metrics.AvailableMemoryGb;
        SharedMemoryUsedMb = metrics.SharedMemoryUsedMb;

        TemperatureC = metrics.TemperatureC;
        HotspotTemperatureC = metrics.HotspotTemperatureC;
        PowerWatts = metrics.PowerWatts;
        PowerTargetWatts = metrics.PowerTargetWatts;

        Engine3DPercent = metrics.Engine3DPercent;
        EngineComputePercent = metrics.EngineComputePercent;
        EngineVideoEncodePercent = metrics.EngineVideoEncodePercent;
        EngineVideoDecodePercent = metrics.EngineVideoDecodePercent;
        EngineCopyPercent = metrics.EngineCopyPercent;

        CoreClockMhz = metrics.CoreClockMhz;
        BoostClockMhz = metrics.BoostClockMhz;
        MemoryClockMhz = metrics.MemoryClockMhz;
        MemoryClockMaxMhz = metrics.MemoryClockMaxMhz;
        ThrottleStatus = metrics.ThrottleStatus;

        FanSpeedRpm = metrics.FanSpeedRpm;
        FanSpeedPercent = metrics.FanSpeedPercent;
        FanProfile = metrics.FanProfile;

        // Update historical ring buffer
        _history3D.AddLast(metrics.UtilizationPercent);
        while (_history3D.Count > MaxHistoryPoints)
        {
            _history3D.RemoveFirst();
        }

        _historyVram.AddLast(metrics.DedicatedMemoryPercent);
        while (_historyVram.Count > MaxHistoryPoints)
        {
            _historyVram.RemoveFirst();
        }

        PeakUtilizationPercent = Math.Round(_history3D.Max(), 1);
        AverageUtilizationPercent = Math.Round(_history3D.Average(), 1);

        UpdateTimelineGeometries();

        // Update Top Processes
        TopProcesses.Clear();
        foreach (var p in metrics.Processes)
        {
            TopProcesses.Add(new GpuProcessItemViewModel
            {
                Pid = p.Pid,
                Name = p.Name,
                Engine = p.Engine,
                UtilizationPercent = p.UtilizationPercent,
                Mode = p.Mode,
                MemoryDisplay = $"{p.MemoryMb:N0} MB"
            });
        }

        // Update Available Devices
        AvailableDevices.Clear();
        foreach (var d in metrics.Devices)
        {
            AvailableDevices.Add(new GpuDeviceItemViewModel
            {
                Index = d.Index,
                Name = d.Name,
                ShortName = d.ShortName,
                DedicatedBytes = d.DedicatedBytes,
                DriverVersion = d.DriverVersion,
                IsDiscrete = d.IsDiscrete,
                IsSelected = d.IsSelected
            });
        }

        StatusText = $"D3D12 // {metrics.AdapterName.ToUpperInvariant()}";
    }

    private void UpdateTimelineGeometries()
    {
        if (_history3D.Count < 2) return;

        const double width = 600.0;
        const double height = 120.0;
        double step = width / (_history3D.Count - 1);

        var points3D = new List<string>();
        var pointsVram = new List<string>();
        var miniPoints = new List<string>();

        const double miniWidth = 90.0;
        const double miniHeight = 22.0;
        double miniStep = miniWidth / (_history3D.Count - 1);

        int index = 0;
        using var vramEnum = _historyVram.GetEnumerator();

        foreach (var val in _history3D)
        {
            vramEnum.MoveNext();
            double vramVal = vramEnum.Current;

            double x = Math.Round(index * step, 1);

            double norm3D = Math.Clamp(val / 100.0, 0.0, 1.0);
            double y3D = Math.Round(height - (norm3D * height), 1);
            points3D.Add($"{x},{y3D}");

            double normVram = Math.Clamp(vramVal / 100.0, 0.0, 1.0);
            double yVram = Math.Round(height - (normVram * height), 1);
            pointsVram.Add($"{x},{yVram}");

            double mx = Math.Round(index * miniStep, 1);
            double my = Math.Round(miniHeight - (norm3D * (miniHeight - 4)) - 2, 1);
            miniPoints.Add($"{mx},{my}");

            index++;
        }

        TimelineLineGeometry = "M " + string.Join(" L ", points3D);
        TimelineAreaGeometry = $"M 0,{height} L " + string.Join(" L ", points3D) + $" L {width},{height} Z";
        TimelineVramGeometry = "M " + string.Join(" L ", pointsVram);
        MiniSparklineGeometry = "M " + string.Join(" L ", miniPoints);
        SparklineGeometry = TimelineLineGeometry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
