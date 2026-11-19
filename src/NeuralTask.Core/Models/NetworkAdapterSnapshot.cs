using System;

namespace NeuralTask.Core.Models;

public record NetworkAdapterSnapshot(
    string Name,
    string Description,
    string InterfaceTypeDisplay,
    long LinkSpeedBitsPerSec,
    string LinkSpeedDisplay
)
{
    public static readonly NetworkAdapterSnapshot Default = new(
        Name: "Network",
        Description: "Active Network Interface",
        InterfaceTypeDisplay: "Ethernet / Wi-Fi",
        LinkSpeedBitsPerSec: 100_000_000,
        LinkSpeedDisplay: "100 Mbps"
    );
}
