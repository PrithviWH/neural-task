using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace NeuralTask.Plugins.Gpu;

/// <summary>
/// Hardware sampler for GPU engine utilization and video memory using
/// Windows Performance Counters and Win32_VideoController WMI.
/// </summary>
public class GpuTelemetrySampler : IDisposable
{
    private string? _cachedAdapterName;
    private long _cachedDedicatedTotalBytes;
    private readonly List<PerformanceCounter> _engineCounters = new();
    private readonly List<PerformanceCounter> _memoryDedicatedCounters = new();
    private readonly List<PerformanceCounter> _memorySharedCounters = new();
    private bool _initialized;
    private readonly object _lock = new();

    public string AdapterName
    {
        get
        {
            if (_cachedAdapterName == null)
            {
                InitializeAdapterInfo();
            }
            return _cachedAdapterName ?? "DirectX Hardware GPU";
        }
    }

    public long DedicatedTotalBytes
    {
        get
        {
            if (_cachedDedicatedTotalBytes <= 0)
            {
                InitializeAdapterInfo();
            }
            return _cachedDedicatedTotalBytes > 0 ? _cachedDedicatedTotalBytes : 4L * 1024 * 1024 * 1024; // Default 4GB
        }
    }

    private void InitializeAdapterInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedAdapterName = name;
                    if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ram) && ram > 0)
                    {
                        _cachedDedicatedTotalBytes = ram;
                    }
                    break;
                }
            }

            // Fallback if only virtual display was found
            if (string.IsNullOrWhiteSpace(_cachedAdapterName))
            {
                _cachedAdapterName = "Primary Graphics Adapter";
            }
        }
        catch
        {
            _cachedAdapterName = "DirectX Hardware GPU";
            _cachedDedicatedTotalBytes = 4L * 1024 * 1024 * 1024;
        }
    }

    private void InitializeCounters()
    {
        if (_initialized) return;

        try
        {
            // 1. GPU Engine Counters
            if (PerformanceCounterCategory.Exists("GPU Engine"))
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();

                // Select representative 3D, Compute, and Video engines (top 12 instances)
                var targetInstances = instanceNames
                    .Where(i => i.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                                i.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ||
                                i.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                    .Take(12)
                    .ToList();

                foreach (var inst in targetInstances)
                {
                    try
                    {
                        var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                        counter.NextValue(); // Prime initial sample
                        _engineCounters.Add(counter);
                    }
                    catch
                    {
                    }
                }
            }

            // 2. GPU Memory Counters
            if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
            {
                var memCat = new PerformanceCounterCategory("GPU Adapter Memory");
                var memInstances = memCat.GetInstanceNames();

                foreach (var inst in memInstances)
                {
                    try
                    {
                        var dedCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, readOnly: true);
                        dedCounter.NextValue();
                        _memoryDedicatedCounters.Add(dedCounter);

                        var sharedCounter = new PerformanceCounter("GPU Adapter Memory", "Shared Usage", inst, readOnly: true);
                        sharedCounter.NextValue();
                        _memorySharedCounters.Add(sharedCounter);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
            // Performance counters might be disabled or restricted; sampler will fallback safely
        }
        finally
        {
            _initialized = true;
        }
    }

    /// <summary>
    /// Samples real-time GPU engine utilization and memory usage.
    /// </summary>
    public GpuMetrics Sample()
    {
        lock (_lock)
        {
            if (!_initialized)
            {
                InitializeCounters();
            }

            double engine3D = 0;
            double engineDecode = 0;
            double engineCompute = 0;
            double engineCopy = 0;

            foreach (var counter in _engineCounters)
            {
                try
                {
                    var val = counter.NextValue();
                    if (val <= 0) continue;

                    var name = counter.InstanceName;
                    if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    {
                        engine3D += val;
                    }
                    else if (name.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase))
                    {
                        engineDecode += val;
                    }
                    else if (name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                    {
                        engineCompute += val;
                    }
                    else if (name.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase))
                    {
                        engineCopy += val;
                    }
                }
                catch
                {
                }
            }

            engine3D = Math.Clamp(Math.Round(engine3D, 1), 0.0, 100.0);
            engineDecode = Math.Clamp(Math.Round(engineDecode, 1), 0.0, 100.0);
            engineCompute = Math.Clamp(Math.Round(engineCompute, 1), 0.0, 100.0);
            engineCopy = Math.Clamp(Math.Round(engineCopy, 1), 0.0, 100.0);

            double overallUtil = Math.Max(engine3D, Math.Max(engineDecode, engineCompute));

            long dedicatedUsed = 0;
            foreach (var counter in _memoryDedicatedCounters)
            {
                try
                {
                    var val = counter.NextValue();
                    if (val > 0) dedicatedUsed += (long)val;
                }
                catch
                {
                }
            }

            long sharedUsed = 0;
            foreach (var counter in _memorySharedCounters)
            {
                try
                {
                    var val = counter.NextValue();
                    if (val > 0) sharedUsed += (long)val;
                }
                catch
                {
                }
            }

            return new GpuMetrics(
                AdapterName: AdapterName,
                UtilizationPercent: overallUtil,
                DedicatedMemoryUsedBytes: dedicatedUsed,
                DedicatedMemoryTotalBytes: DedicatedTotalBytes,
                SharedMemoryUsedBytes: sharedUsed,
                Engine3DPercent: engine3D,
                EngineVideoDecodePercent: engineDecode,
                EngineCopyPercent: engineCopy,
                EngineComputePercent: engineCompute,
                Timestamp: DateTime.UtcNow);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var counter in _engineCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _engineCounters.Clear();

            foreach (var counter in _memoryDedicatedCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _memoryDedicatedCounters.Clear();

            foreach (var counter in _memorySharedCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _memorySharedCounters.Clear();

            _initialized = false;
        }
    }
}
