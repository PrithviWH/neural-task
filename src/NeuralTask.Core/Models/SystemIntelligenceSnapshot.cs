using System;

namespace NeuralTask.Core.Models;

public enum HealthState
{
    Optimal,
    Elevated,
    HeavyLoad
}

public record SystemIntelligenceSnapshot(
    TimeSpan Uptime,
    string HardwareSummary,
    HealthState HealthState,
    string HealthStateDisplay,
    string HealthMessage,
    double PeakCpuRolling,
    double PeakRamRolling,
    string RecentSpikesSummary,
    string StartupImpactSummary
);
