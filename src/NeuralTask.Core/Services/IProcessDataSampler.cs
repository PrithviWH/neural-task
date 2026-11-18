using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Abstraction for gathering raw process state from the OS or simulated sources.
/// </summary>
public interface IProcessDataSampler
{
    IReadOnlyList<RawProcessSample> CollectRawSamples();
}
