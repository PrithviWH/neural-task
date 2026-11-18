using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service interface for querying genuine system-wide performance and hardware metrics.
/// </summary>
public interface ISystemMetricsService
{
    SystemMetricsSnapshot CollectSnapshot(IReadOnlyList<ProcessSnapshot> processes);
}
