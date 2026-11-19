using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public interface IStorageInfoService
{
    StorageMetricsSnapshot GetStorageSnapshot();
}
