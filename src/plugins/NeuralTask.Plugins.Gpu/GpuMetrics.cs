using System;
using System.Collections.Generic;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// Summary item for an available GPU adapter on the system.
/// </summary>
public record GpuDeviceItem(
    int Index,
    string Name,
    string ShortName,
    long DedicatedBytes,
    string DriverVersion,
    bool IsDiscrete,
    bool IsSelected);

/// <summary>
/// Process-level GPU utilization entry.
/// </summary>
public record GpuProcessItem(
    int Pid,
    string Name,
    string Engine,
    double UtilizationPercent,
    string Mode,
    double MemoryMb);

/// <summary>
/// Snapshot of GPU hardware acceleration, video memory, thermals, power, and clock metrics.
/// </summary>
public record GpuMetrics(
    string AdapterName,
    double UtilizationPercent,
    long DedicatedMemoryUsedBytes,
    long DedicatedMemoryTotalBytes,
    long SharedMemoryUsedBytes,
    double Engine3DPercent,
    double EngineVideoDecodePercent,
    double EngineCopyPercent,
    double EngineComputePercent,
    DateTime Timestamp,
    double EngineVideoEncodePercent = 0.0,
    double TemperatureC = 48.0,
    double HotspotTemperatureC = 52.0,
    double PowerWatts = 28.0,
    double PowerTargetWatts = 80.0,
    int CoreClockMhz = 1425,
    int BoostClockMhz = 2100,
    int MemoryClockMhz = 7151,
    int MemoryClockMaxMhz = 7500,
    string ThrottleStatus = "Optimal • P-State P0",
    int FanSpeedRpm = 0,
    double FanSpeedPercent = 0.0,
    string FanProfile = "Auto Curve (Zero-dB Silent Mode)",
    string DriverVersion = "WDDM 3.1 Driver",
    string VideoProcessor = "NVIDIA GeForce RTX 3060 Laptop GPU",
    string DisplayResolution = "1920 x 1080 @ 144Hz",
    string DirectXLevel = "Direct3D 12.2 • Feature Level 12_2 (DirectX 12 Ultimate)",
    string Status = "Direct3D Hardware Acceleration OK",
    IReadOnlyList<GpuProcessItem>? TopProcesses = null,
    IReadOnlyList<GpuDeviceItem>? AvailableDevices = null,
    int SelectedDeviceIndex = 0)
{
    public double DedicatedMemoryUsedMb => Math.Round(DedicatedMemoryUsedBytes / (1024.0 * 1024.0), 1);
    public double DedicatedMemoryTotalMb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0), 1);
    public double DedicatedMemoryUsedGb => Math.Round(DedicatedMemoryUsedBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double DedicatedMemoryTotalGb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double AvailableMemoryMb => Math.Max(0, DedicatedMemoryTotalMb - DedicatedMemoryUsedMb);
    public double AvailableMemoryGb => Math.Max(0.0, Math.Round(DedicatedMemoryTotalGb - DedicatedMemoryUsedGb, 1));
    public double SharedMemoryUsedMb => Math.Round(SharedMemoryUsedBytes / (1024.0 * 1024.0), 1);

    public double DedicatedMemoryPercent =>
        DedicatedMemoryTotalBytes > 0
            ? Math.Clamp(Math.Round((double)DedicatedMemoryUsedBytes / DedicatedMemoryTotalBytes * 100.0, 1), 0.0, 100.0)
            : 0.0;

    public IReadOnlyList<GpuProcessItem> Processes => TopProcesses ?? Array.Empty<GpuProcessItem>();
    public IReadOnlyList<GpuDeviceItem> Devices => AvailableDevices ?? Array.Empty<GpuDeviceItem>();
}
