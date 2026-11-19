using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service contract for sampling GPU engine utilization, VRAM usage, and active GPU processes.
/// </summary>
public interface IGpuMetricsService
{
    bool IsSupported { get; }
    string AdapterName { get; }
    GpuMetricsSnapshot CollectSnapshot(IReadOnlyList<ProcessSnapshot> processes);
}
