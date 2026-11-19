using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public interface INetworkInfoService
{
    NetworkAdapterSnapshot GetPrimaryAdapterInfo();
}
