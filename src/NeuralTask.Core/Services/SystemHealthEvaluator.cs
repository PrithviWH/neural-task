using System;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public static class SystemHealthEvaluator
{
    public static SystemIntelligenceSnapshot Evaluate(
        SystemMetricsSnapshot system,
        GpuMetricsSnapshot? gpu,
        double peakCpu,
        double peakRam,
        int startupAppCount = 0,
        int startupAverageScore = 0)
    {
        var healthState = HealthState.Optimal;
        string stateDisplay = "Optimal";
        string message = "All subsystems operating within normal parameters.";

        if (system.CpuPercent >= 90.0 || system.MemoryPercent >= 92.0)
        {
            healthState = HealthState.HeavyLoad;
            stateDisplay = "High Load";
            message = system.CpuPercent >= 90.0
                ? "CPU saturation detected across available compute cores."
                : "Severe memory pressure detected. System swapping may occur.";
        }
        else if (system.CpuPercent >= 70.0 || system.MemoryPercent >= 85.0)
        {
            healthState = HealthState.Elevated;
            stateDisplay = "Elevated";
            message = system.CpuPercent >= 70.0
                ? "Elevated CPU workload from active processes."
                : "Moderate physical memory pressure.";
        }

        double totalRamGb = Math.Round(system.TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0), 1);
        string gpuName = gpu?.IsSupported == true ? gpu.AdapterName : "Integrated Display";
        string hwSummary = $"{system.LogicalCoreCount} Cores • {totalRamGb:F1} GB RAM • {gpuName}";

        string spikesSummary = $"Peak CPU: {peakCpu:F1}% • Peak RAM: {peakRam:F1}%";

        string startupSummary = startupAppCount > 0
            ? $"{startupAppCount} startup apps • {startupAverageScore}/100 boot score"
            : "No high-impact startup apps detected";

        return new SystemIntelligenceSnapshot(
            Uptime: system.Uptime,
            HardwareSummary: hwSummary,
            HealthState: healthState,
            HealthStateDisplay: stateDisplay,
            HealthMessage: message,
            PeakCpuRolling: peakCpu,
            PeakRamRolling: peakRam,
            RecentSpikesSummary: spikesSummary,
            StartupImpactSummary: startupSummary
        );
    }
}
