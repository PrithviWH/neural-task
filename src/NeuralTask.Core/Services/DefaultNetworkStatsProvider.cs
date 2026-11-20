using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace NeuralTask.Core.Services;

/// <summary>
/// Production network stats provider querying native Windows network interfaces.
/// Prioritizes modern 64-bit IPInterfaceStatistics (MIB_IF_ROW2) to capture IPv4 and IPv6 traffic combined,
/// with safe fallback to IPv4 statistics on legacy or virtual miniports.
/// </summary>
public class DefaultNetworkStatsProvider : INetworkStatsProvider
{
    public static readonly DefaultNetworkStatsProvider Instance = new();

    public IReadOnlyList<NetworkInterfaceStats> GetActiveInterfaceStats()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var results = new List<NetworkInterfaceStats>();

            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up ||
                    iface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                bool supportsIPv4 = iface.Supports(NetworkInterfaceComponent.IPv4);
                bool supportsIPv6 = iface.Supports(NetworkInterfaceComponent.IPv6);

                ulong bytesReceived = 0;
                ulong bytesSent = 0;
                bool statsRetrieved = false;

                // Priority 1: Modern 64-bit IPInterfaceStatistics (captures IPv4 + IPv6 combined)
                try
                {
                    var ipStats = iface.GetIPStatistics();
                    if (ipStats != null)
                    {
                        long rx = ipStats.BytesReceived;
                        long tx = ipStats.BytesSent;

                        if (rx >= 0 && tx >= 0)
                        {
                            bytesReceived = (ulong)rx;
                            bytesSent = (ulong)tx;
                            statsRetrieved = true;
                        }
                    }
                }
                catch
                {
                    // Some virtual or legacy miniports may throw on GetIPStatistics
                }

                // Priority 2: Fallback to IPv4 statistics if GetIPStatistics was unavailable
                if (!statsRetrieved && supportsIPv4)
                {
                    try
                    {
                        var v4Stats = iface.GetIPv4Statistics();
                        if (v4Stats != null)
                        {
                            long rx = v4Stats.BytesReceived;
                            long tx = v4Stats.BytesSent;
                            if (rx >= 0 && tx >= 0)
                            {
                                bytesReceived = (ulong)rx;
                                bytesSent = (ulong)tx;
                                statsRetrieved = true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (statsRetrieved)
                {
                    results.Add(new NetworkInterfaceStats(
                        Id: iface.Id,
                        Name: iface.Name,
                        BytesReceived: bytesReceived,
                        BytesSent: bytesSent,
                        SupportsIPv4: supportsIPv4,
                        SupportsIPv6: supportsIPv6,
                        OperationalStatus: iface.OperationalStatus
                    ));
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<NetworkInterfaceStats>();
        }
    }
}
