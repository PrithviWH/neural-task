using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class NetworkMetricsTests
{
    private class FakeTimeSource : ITimeSource
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public long TimestampTicks { get; set; } = 10_000_000;
        public double TicksPerSecond => 10_000_000.0; // 10 million ticks per second (100ns per tick)

        public void AdvanceSeconds(double seconds)
        {
            TimestampTicks += (long)(seconds * TicksPerSecond);
            UtcNow = UtcNow.AddSeconds(seconds);
        }
    }

    private class FakeNetworkStatsProvider : INetworkStatsProvider
    {
        public List<NetworkInterfaceStats> Interfaces { get; } = new();

        public IReadOnlyList<NetworkInterfaceStats> GetActiveInterfaceStats()
        {
            return Interfaces.ToArray();
        }
    }

    [Fact]
    public void CollectSnapshot_IPv4OnlyInterface_MeasuresThroughputAccurately()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats(
            Id: "eth0_v4",
            Name: "Ethernet 1",
            BytesReceived: 100_000,
            BytesSent: 50_000,
            SupportsIPv4: true,
            SupportsIPv6: false
        ));

        // Sample 1: Baseline
        var snap1 = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(0.0, snap1.NetworkReceivedBytesPerSec);
        Assert.Equal(0.0, snap1.NetworkSentBytesPerSec);

        // Advance 1.0 second
        time.AdvanceSeconds(1.0);
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 150_000, // +50,000
            BytesSent = 75_000       // +25,000
        };

        // Sample 2: 50 KB/s in, 25 KB/s out
        var snap2 = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(50_000.0, snap2.NetworkReceivedBytesPerSec);
        Assert.Equal(25_000.0, snap2.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_IPv6OnlyInterface_MeasuresThroughputAccurately()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats(
            Id: "wlan0_v6",
            Name: "Wi-Fi IPv6",
            BytesReceived: 1_000_000,
            BytesSent: 400_000,
            SupportsIPv4: false,
            SupportsIPv6: true
        ));

        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        // Advance 2.0 seconds
        time.AdvanceSeconds(2.0);
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 1_400_000, // +400,000 over 2s = 200,000/s
            BytesSent = 500_000        // +100,000 over 2s = 50,000/s
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(200_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(50_000.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_DualStackInterface_MeasuresCombinedTraffic()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats(
            Id: "dual0",
            Name: "Dual Stack NIC",
            BytesReceived: 5_000_000,
            BytesSent: 2_000_000,
            SupportsIPv4: true,
            SupportsIPv6: true
        ));

        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 5_800_000, // +800,000
            BytesSent = 2_300_000       // +300,000
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(800_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(300_000.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_MultipleInterfaces_AggregatesAccuratelyWithoutDuplication()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic1", "Ethernet", 10_000, 5_000, true, true));
        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic2", "Wi-Fi", 20_000, 10_000, true, true));
        // Add duplicate entry with same ID to test deduplication guard
        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic1", "Ethernet Duplicate", 10_000, 5_000, true, true));

        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        netProvider.Interfaces.Clear();
        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic1", "Ethernet", 30_000, 15_000, true, true)); // delta 20k, 10k
        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic2", "Wi-Fi", 60_000, 30_000, true, true));    // delta 40k, 20k
        netProvider.Interfaces.Add(new NetworkInterfaceStats("nic1", "Ethernet Duplicate", 30_000, 15_000, true, true));

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        // nic1 (20k) + nic2 (40k) = 60k RX; nic1 (10k) + nic2 (20k) = 30k TX
        Assert.Equal(60_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(30_000.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_Large64BitCounters_CalculatesDeltasWithoutTruncationOrOverflow()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        // 5 Terabytes (> 4 GB 32-bit limit)
        ulong initial5Tb = 5_000_000_000_000UL;
        netProvider.Interfaces.Add(new NetworkInterfaceStats("fiber10g", "10G Fiber", initial5Tb, initial5Tb, true, true));

        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // Transferred 100 MB
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = initial5Tb + 100_000_000UL,
            BytesSent = initial5Tb + 50_000_000UL
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(100_000_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(50_000_000.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_32BitCounterRollover_ComputesAccurateDelta()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        // 32-bit counter just below uint.MaxValue (4,294,967,295)
        ulong preRollRx = 4_294_960_000UL;
        ulong preRollTx = 4_294_950_000UL;
        netProvider.Interfaces.Add(new NetworkInterfaceStats("legacy_nic", "FastEthernet", preRollRx, preRollTx, true, false));

        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // Wrapped around 0: current is 7,296 RX and 5,000 TX
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 7_296UL,
            BytesSent = 5_000UL
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        // Rollover RX: (4,294,967,295 - 4,294,960,000) + 7,296 + 1 = 7,295 + 7,296 + 1 = 14,592
        // Rollover TX: (4,294,967,295 - 4,294,950,000) + 5,000 + 1 = 17,295 + 5,000 + 1 = 22,296
        Assert.Equal(14_592.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(22_296.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_CounterResetOrReboot_DoesNotReportNegativeOrGiantSpikes()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        // Active interface at 50 MB
        netProvider.Interfaces.Add(new NetworkInterfaceStats("resettable", "NIC", 50_000_000UL, 25_000_000UL, true, true));
        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // Adapter was reset/restarted: counters drop to near 0 (NOT a 32-bit wrap because previous was only 50MB)
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 1_000UL,
            BytesSent = 500UL
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        // Must safely report 0.0 and reset baseline rather than producing negative or false 4GB rate
        Assert.Equal(0.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(0.0, snap.NetworkSentBytesPerSec);

        // Next tick: computes genuine delta from the new baseline (1,000 -> 11,000 = 10,000)
        time.AdvanceSeconds(1.0);
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 11_000UL,
            BytesSent = 5_500UL
        };

        var snapNext = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(10_000.0, snapNext.NetworkReceivedBytesPerSec);
        Assert.Equal(5_000.0, snapNext.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_InterfaceAddition_DoesNotProduceFalseSpike()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats("eth0", "Ethernet", 10_000, 5_000, true, true));
        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // eth0 transfers 5,000 bytes. Simultaneously, Wi-Fi connects with 8 Gigabytes already on counter
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with { BytesReceived = 15_000, BytesSent = 7_500 };
        netProvider.Interfaces.Add(new NetworkInterfaceStats("wlan0", "Wi-Fi", 8_000_000_000UL, 4_000_000_000UL, true, true));

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        // Only eth0's delta (5,000 RX / 2,500 TX) should be counted!
        // Wi-Fi's 8 GB initial baseline must NOT cause a false 8 GB/s spike.
        Assert.Equal(5_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(2_500.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_InterfaceRemoval_ContinuesAccurateSamplingOnRemainingInterfaces()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats("eth0", "Ethernet", 10_000, 5_000, true, true));
        netProvider.Interfaces.Add(new NetworkInterfaceStats("wlan0", "Wi-Fi", 100_000, 50_000, true, true));
        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // eth0 is unplugged / removed from active list. wlan0 transfers 20,000 bytes.
        netProvider.Interfaces.Clear();
        netProvider.Interfaces.Add(new NetworkInterfaceStats("wlan0", "Wi-Fi", 120_000, 60_000, true, true));

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        // Removing eth0 must not disrupt wlan0's delta calculation
        Assert.Equal(20_000.0, snap.NetworkReceivedBytesPerSec);
        Assert.Equal(10_000.0, snap.NetworkSentBytesPerSec);
    }

    [Fact]
    public void CollectSnapshot_ImplausiblyLargeGlitch_IsDiscardedSafely()
    {
        var time = new FakeTimeSource();
        var netProvider = new FakeNetworkStatsProvider();
        var service = new SystemMetricsService(time, netProvider);

        netProvider.Interfaces.Add(new NetworkInterfaceStats("glitch_nic", "Faulty Driver", 1_000, 500, true, true));
        service.CollectSnapshot(Array.Empty<ProcessSnapshot>());

        time.AdvanceSeconds(1.0);
        // Driver glitch returns an impossible delta: > 100 Gigabits in 1 second
        netProvider.Interfaces[0] = netProvider.Interfaces[0] with
        {
            BytesReceived = 1_000UL + 50_000_000_000UL // 50 GB in 1 second
        };

        var snap = service.CollectSnapshot(Array.Empty<ProcessSnapshot>());
        Assert.Equal(0.0, snap.NetworkReceivedBytesPerSec);
    }
}
