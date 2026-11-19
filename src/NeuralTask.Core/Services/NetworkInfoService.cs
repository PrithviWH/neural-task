using System;
using System.Linq;
using System.Net.NetworkInformation;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public class NetworkInfoService : INetworkInfoService
{
    public static readonly NetworkInfoService Instance = new();

    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private NetworkAdapterSnapshot? _cached;
    private readonly object _lock = new();

    public NetworkAdapterSnapshot GetPrimaryAdapterInfo()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.Now;
            if (_cached != null && (now - _lastCheck).TotalSeconds < 5)
            {
                return _cached;
            }

            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                var activeNic = nics.FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.Speed > 0);

                activeNic ??= nics.FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                if (activeNic != null)
                {
                    string typeDisplay = activeNic.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => "Wi-Fi Wireless",
                        NetworkInterfaceType.Ethernet => "Gigabit Ethernet",
                        NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
                        _ => activeNic.NetworkInterfaceType.ToString()
                    };

                    string speedDisplay = FormatSpeed(activeNic.Speed);

                    _cached = new NetworkAdapterSnapshot(
                        Name: activeNic.Name,
                        Description: activeNic.Description,
                        InterfaceTypeDisplay: typeDisplay,
                        LinkSpeedBitsPerSec: activeNic.Speed,
                        LinkSpeedDisplay: speedDisplay
                    );
                    _lastCheck = now;
                    return _cached;
                }
            }
            catch
            {
            }

            _cached = NetworkAdapterSnapshot.Default;
            _lastCheck = now;
            return _cached;
        }
    }

    private static string FormatSpeed(long speedBits)
    {
        if (speedBits <= 0) return "Link active";
        if (speedBits >= 1_000_000_000)
        {
            return $"{speedBits / 1_000_000_000.0:F1} Gbps";
        }
        if (speedBits >= 1_000_000)
        {
            return $"{speedBits / 1_000_000} Mbps";
        }
        return $"{speedBits / 1000} Kbps";
    }
}
