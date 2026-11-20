using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NeuralTask.Plugins.Gpu;

public interface IGpuCounter : IDisposable
{
    double NextValue();
}

public interface IGpuPerformanceCounterProvider
{
    bool IsSupported { get; }
    IReadOnlyList<string> GetEngineInstanceNames();
    IReadOnlyList<string> GetMemoryInstanceNames();
    IGpuCounter? CreateEngineCounter(string instanceName);
    IGpuCounter? CreateMemoryCounter(string instanceName, string counterName);
}

public class StandardGpuCounter : IGpuCounter
{
    private readonly PerformanceCounter _counter;

    public StandardGpuCounter(PerformanceCounter counter)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
    }

    public double NextValue()
    {
        return _counter.NextValue();
    }

    public void Dispose()
    {
        _counter.Dispose();
    }
}

public class WindowsGpuPerformanceCounterProvider : IGpuPerformanceCounterProvider
{
    public static readonly WindowsGpuPerformanceCounterProvider Instance = new();

    public bool IsSupported =>
        OperatingSystem.IsWindows() && PerformanceCounterCategory.Exists("GPU Engine");

    public IReadOnlyList<string> GetEngineInstanceNames()
    {
        if (!IsSupported) return Array.Empty<string>();
        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            return cat.GetInstanceNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetMemoryInstanceNames()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                return Array.Empty<string>();
            var cat = new PerformanceCounterCategory("GPU Adapter Memory");
            return cat.GetInstanceNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public IGpuCounter? CreateEngineCounter(string instanceName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, readOnly: true);
            c.NextValue(); // Prime initial sample
            return new StandardGpuCounter(c);
        }
        catch
        {
            return null;
        }
    }

    public IGpuCounter? CreateMemoryCounter(string instanceName, string counterName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var c = new PerformanceCounter("GPU Adapter Memory", counterName, instanceName, readOnly: true);
            c.NextValue(); // Prime initial sample
            return new StandardGpuCounter(c);
        }
        catch
        {
            return null;
        }
    }
}

public static class GpuInstanceParser
{
    private static readonly Regex PidRegex = new(@"pid_(\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LuidRegex = new(@"luid_(0x[0-9a-fA-F]+(?:_0x[0-9a-fA-F]+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EngIndexRegex = new(@"_eng_(\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParseEngineInstance(
        string instanceName,
        out int pid,
        out string luid,
        out string engineCategory,
        out int engineIndex)
    {
        pid = 0;
        luid = string.Empty;
        engineCategory = "3D";
        engineIndex = 0;

        if (string.IsNullOrWhiteSpace(instanceName)) return false;

        var pidMatch = PidRegex.Match(instanceName);
        if (pidMatch.Success && int.TryParse(pidMatch.Groups[1].Value, out var parsedPid))
        {
            pid = parsedPid;
        }

        var luidMatch = LuidRegex.Match(instanceName);
        if (luidMatch.Success)
        {
            luid = luidMatch.Groups[1].Value.ToLowerInvariant();
        }

        var engMatch = EngIndexRegex.Match(instanceName);
        if (engMatch.Success && int.TryParse(engMatch.Groups[1].Value, out var parsedEng))
        {
            engineIndex = parsedEng;
        }

        if (instanceName.Contains("engtype_Video Codec", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_VideoEncode", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "VideoEncode";
        }
        else if (instanceName.Contains("engtype_Video Decode", StringComparison.OrdinalIgnoreCase) ||
                 instanceName.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ||
                 instanceName.Contains("engtype_Video", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "VideoDecode";
        }
        else if (instanceName.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "Compute";
        }
        else if (instanceName.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "Copy";
        }
        else if (instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "3D";
        }
        else if (instanceName.Contains("engtype_VR", StringComparison.OrdinalIgnoreCase))
        {
            engineCategory = "VR";
        }
        else
        {
            var typeMatch = Regex.Match(instanceName, @"engtype_([a-zA-Z0-9_\s]+)$", RegexOptions.IgnoreCase);
            if (typeMatch.Success)
            {
                engineCategory = typeMatch.Groups[1].Value.Trim();
            }
        }

        return true;
    }

    public static bool TryParseMemoryInstance(string instanceName, out string luid)
    {
        luid = string.Empty;
        if (string.IsNullOrWhiteSpace(instanceName)) return false;

        var luidMatch = LuidRegex.Match(instanceName);
        if (luidMatch.Success)
        {
            luid = luidMatch.Groups[1].Value.ToLowerInvariant();
            return true;
        }

        return false;
    }
}
