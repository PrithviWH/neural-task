using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace NeuralTask.Core.Services;

/// <summary>
/// Represents a snapshot of 64-bit byte counters and capabilities for a network interface.
/// </summary>
public record NetworkInterfaceStats(
    string Id,
    string Name,
    ulong BytesReceived,
    ulong BytesSent,
    bool SupportsIPv4,
    bool SupportsIPv6,
    OperationalStatus OperationalStatus = OperationalStatus.Up
);

/// <summary>
/// Abstraction for discovering active network interfaces and querying 64-bit traffic counters.
/// Enables deterministic unit testing of IPv4, IPv6, rollover, interface additions, and removals.
/// </summary>
public interface INetworkStatsProvider
{
    IReadOnlyList<NetworkInterfaceStats> GetActiveInterfaceStats();
}
