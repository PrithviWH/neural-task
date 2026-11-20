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
    double? UtilizationPercent,
    string Mode,
    double? MemoryMb = null);

/// <summary>
/// Snapshot of GPU hardware acceleration, video memory, thermals, power, and clock metrics.
/// </summary>
public record GpuMetrics(
    string AdapterName,
    double? UtilizationPercent,
    long? DedicatedMemoryUsedBytes,
    long DedicatedMemoryTotalBytes,
    long? SharedMemoryUsedBytes,
    double? Engine3DPercent,
    double? EngineVideoDecodePercent,
    double? EngineCopyPercent,
    double? EngineComputePercent,
    DateTime Timestamp,
    double? EngineVideoEncodePercent = null,
    double? TemperatureC = null,
    double? HotspotTemperatureC = null,
    double? PowerWatts = null,
    double PowerTargetWatts = 80.0,
    int? CoreClockMhz = null,
    int? BoostClockMhz = null,
    int? MemoryClockMhz = null,
    int? MemoryClockMaxMhz = null,
    double? VoltageV = null,
    string ThrottleStatus = "Optimal • P-State P0",
    int? FanSpeedRpm = null,
    double? FanSpeedPercent = null,
    string? FanProfile = null,
    string DriverVersion = "WDDM 3.1 Driver",
    string VideoProcessor = "Hardware GPU",
    string DisplayResolution = "1920 x 1080 @ 144Hz",
    string DirectXLevel = "Direct3D 12.2 • Feature Level 12_2",
    string Status = "Direct3D Hardware Acceleration OK",
    IReadOnlyList<GpuProcessItem>? TopProcesses = null,
    IReadOnlyList<GpuDeviceItem>? AvailableDevices = null,
    int SelectedDeviceIndex = 0)
{
    public double? DedicatedMemoryUsedMb => DedicatedMemoryUsedBytes.HasValue ? Math.Round(DedicatedMemoryUsedBytes.Value / (1024.0 * 1024.0), 1) : null;
    public double DedicatedMemoryTotalMb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0), 1);
    public double? DedicatedMemoryUsedGb => DedicatedMemoryUsedBytes.HasValue ? Math.Round(DedicatedMemoryUsedBytes.Value / (1024.0 * 1024.0 * 1024.0), 2) : null;
    public double DedicatedMemoryTotalGb => Math.Round(DedicatedMemoryTotalBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double? AvailableMemoryMb => DedicatedMemoryUsedMb.HasValue ? Math.Max(0, DedicatedMemoryTotalMb - DedicatedMemoryUsedMb.Value) : null;
    public double? AvailableMemoryGb => DedicatedMemoryUsedGb.HasValue ? Math.Max(0.0, Math.Round(DedicatedMemoryTotalGb - DedicatedMemoryUsedGb.Value, 1)) : null;
    public double? SharedMemoryUsedMb => SharedMemoryUsedBytes.HasValue ? Math.Round(SharedMemoryUsedBytes.Value / (1024.0 * 1024.0), 1) : null;

    public double? DedicatedMemoryPercent =>
        DedicatedMemoryUsedBytes.HasValue && DedicatedMemoryTotalBytes > 0
            ? Math.Clamp(Math.Round((double)DedicatedMemoryUsedBytes.Value / DedicatedMemoryTotalBytes * 100.0, 1), 0.0, 100.0)
            : null;

    public IReadOnlyList<GpuProcessItem> Processes => TopProcesses ?? Array.Empty<GpuProcessItem>();
    public IReadOnlyList<GpuDeviceItem> Devices => AvailableDevices ?? Array.Empty<GpuDeviceItem>();
}
