using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.Plugins.Gpu;
using Xunit;

namespace NeuralTask.Core.Tests;

public class GpuCounterDiscoveryTests
{
    private class MockGpuCounter : IGpuCounter
    {
        public double Value { get; set; }
        public Func<double>? ValueFactory { get; set; }
        public bool Disposed { get; private set; }

        public MockGpuCounter(double initialValue = 0.0)
        {
            Value = initialValue;
        }

        public double NextValue()
        {
            if (ValueFactory != null) return ValueFactory();
            return Value;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private class MockGpuPerformanceCounterProvider : IGpuPerformanceCounterProvider
    {
        public bool IsSupported { get; set; } = true;
        public List<string> EngineInstances { get; set; } = new();
        public List<string> MemoryInstances { get; set; } = new();
        public Dictionary<string, MockGpuCounter> EngineCounters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MockGpuCounter> MemoryCounters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int EngineQueryCount { get; private set; }
        public int MemoryQueryCount { get; private set; }

        public IReadOnlyList<string> GetEngineInstanceNames()
        {
            EngineQueryCount++;
            return EngineInstances;
        }

        public IReadOnlyList<string> GetMemoryInstanceNames()
        {
            MemoryQueryCount++;
            return MemoryInstances;
        }

        public IGpuCounter? CreateEngineCounter(string instanceName)
        {
            if (!EngineCounters.TryGetValue(instanceName, out var counter))
            {
                counter = new MockGpuCounter();
                EngineCounters[instanceName] = counter;
            }
            return counter;
        }

        public IGpuCounter? CreateMemoryCounter(string instanceName, string counterName)
        {
            string key = $"{instanceName}#{counterName}";
            if (!MemoryCounters.TryGetValue(key, out var counter))
            {
                counter = new MockGpuCounter();
                MemoryCounters[key] = counter;
            }
            return counter;
        }
    }

    private class MockGpuAdapterResolver : IGpuAdapterResolver
    {
        public List<GpuAdapterIdentity> Adapters { get; } = new();

        public IReadOnlyList<GpuAdapterIdentity> DiscoverAdapters() => Adapters;
    }

    private static readonly GpuDeviceInfo AdapterA = new(
        Name: "NVIDIA GeForce RTX 4090",
        ShortName: "RTX 4090",
        DedicatedBytes: 24L * 1024 * 1024 * 1024,
        DriverVersion: "v537.13",
        VideoProcessor: "GA102",
        IsDiscrete: true,
        PowerTargetWatts: 450.0,
        BoostClockMhz: 2520,
        MemoryClockMhz: 10500,
        DirectXLevel: "Direct3D 12.2",
        Luid: "0x00000000_0x00010001");

    private static readonly GpuDeviceInfo AdapterB = new(
        Name: "Intel UHD Graphics 770",
        ShortName: "Intel UHD",
        DedicatedBytes: 1024L * 1024 * 1024,
        DriverVersion: "v31.0.101.4575",
        VideoProcessor: "Intel Gen12",
        IsDiscrete: false,
        PowerTargetWatts: 35.0,
        BoostClockMhz: 1550,
        MemoryClockMhz: 3200,
        DirectXLevel: "Direct3D 12.1",
        Luid: "0x00000000_0x00020002");

    [Fact]
    public void GpuSampler_MultipleAdaptersAndEngines_SegregatedAccurately()
    {
        var provider = new MockGpuPerformanceCounterProvider();

        // Adapter A instances (NVIDIA)
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_1_engtype_Compute");
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_2_engtype_VideoEncode");
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_3_engtype_Copy");

        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(65.0);
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_1_engtype_Compute"] = new MockGpuCounter(25.0);
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_2_engtype_VideoEncode"] = new MockGpuCounter(15.0);
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_3_engtype_Copy"] = new MockGpuCounter(5.0);

        // Adapter B instances (Intel)
        provider.EngineInstances.Add("pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineInstances.Add("pid_200_luid_0x00000000_0x00020002_phys_0_eng_1_engtype_Compute");

        provider.EngineCounters["pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(30.0);
        provider.EngineCounters["pid_200_luid_0x00000000_0x00020002_phys_0_eng_1_engtype_Compute"] = new MockGpuCounter(8.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA, AdapterB }, counterProvider: provider);

        // Sample Adapter A
        sampler.SelectedDeviceIndex = 0;
        var sampleA = sampler.Sample();
        Assert.Equal(65.0, sampleA.Engine3DPercent);
        Assert.Equal(25.0, sampleA.EngineComputePercent);
        Assert.Equal(15.0, sampleA.EngineVideoEncodePercent);
        Assert.Equal(5.0, sampleA.EngineCopyPercent);
        Assert.Equal(65.0, sampleA.UtilizationPercent);

        // Sample Adapter B
        sampler.SelectedDeviceIndex = 1;
        var sampleB = sampler.Sample();
        Assert.Equal(30.0, sampleB.Engine3DPercent);
        Assert.Equal(8.0, sampleB.EngineComputePercent);
        Assert.Null(sampleB.EngineVideoEncodePercent);
        Assert.Null(sampleB.EngineCopyPercent);
        Assert.Equal(30.0, sampleB.UtilizationPercent);
    }

    [Fact]
    public void GpuSampler_StableAdapterToCounterMapping_RegardlessOfOrder()
    {
        var provider = new MockGpuPerformanceCounterProvider();

        // Place Adapter B first in the enumeration
        provider.EngineInstances.Add("pid_50_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_50_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(12.0);

        provider.EngineInstances.Add("pid_50_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_50_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(88.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA, AdapterB }, counterProvider: provider);

        sampler.SelectedDeviceIndex = 0; // Adapter A
        var sampleA = sampler.Sample();
        Assert.Equal(88.0, sampleA.Engine3DPercent);

        sampler.SelectedDeviceIndex = 1; // Adapter B
        var sampleB = sampler.Sample();
        Assert.Equal(12.0, sampleB.Engine3DPercent);
    }

    [Fact]
    public void GpuSampler_AdapterAdditionAndRemoval_HandledGracefully()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_1_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_1_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(50.0);

        // Initial setup with Adapter A
        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);
        var sample1 = sampler.Sample();
        Assert.Equal(50.0, sample1.Engine3DPercent);

        // Add Adapter B instances dynamically
        provider.EngineInstances.Add("pid_2_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_2_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(70.0);

        sampler.RefreshCounters(force: true);
        var sample2 = sampler.Sample();
        Assert.Equal(50.0, sample2.Engine3DPercent); // Adapter A still 50%

        // Remove Adapter A instance (e.g. driver reset / adapter detached)
        provider.EngineInstances.RemoveAt(0);
        sampler.RefreshCounters(force: true);
        var sample3 = sampler.Sample();
        Assert.Null(sample3.Engine3DPercent); // Stale counter pruned cleanly; missing counters produce null
    }

    [Fact]
    public void GpuSampler_CounterInstanceRenamingOrReordering_DoesNotBreakTelemetry()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineInstances.Add("pid_20_luid_0x00000000_0x00010001_phys_0_eng_1_engtype_Compute");

        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(40.0);
        provider.EngineCounters["pid_20_luid_0x00000000_0x00010001_phys_0_eng_1_engtype_Compute"] = new MockGpuCounter(20.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        var sample1 = sampler.Sample();
        Assert.Equal(40.0, sample1.Engine3DPercent);
        Assert.Equal(20.0, sample1.EngineComputePercent);

        // Reverse instance order in provider
        provider.EngineInstances.Reverse();
        sampler.RefreshCounters(force: true);

        var sample2 = sampler.Sample();
        Assert.Equal(40.0, sample2.Engine3DPercent);
        Assert.Equal(20.0, sample2.EngineComputePercent);
    }

    [Fact]
    public void GpuSampler_MissingOrUnsupportedCounters_HandledGracefully()
    {
        var provider = new MockGpuPerformanceCounterProvider { IsSupported = false };
        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        var sample = sampler.Sample();
        Assert.NotNull(sample);
        Assert.Null(sample.Engine3DPercent);
        Assert.Null(sample.EngineComputePercent);
        Assert.Null(sample.UtilizationPercent);
    }

    [Fact]
    public void GpuSampler_ProcessSpecificInstanceMapping_StrictlySegregatedByAdapter()
    {
        var provider = new MockGpuPerformanceCounterProvider();

        // Process 1234 using Adapter A
        provider.EngineInstances.Add("pid_1234_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_1234_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(85.0);

        // Process 5678 using Adapter B
        provider.EngineInstances.Add("pid_5678_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_5678_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(95.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA, AdapterB }, counterProvider: provider);

        // Sample Adapter A: must ONLY include Process 1234, NOT 5678
        sampler.SelectedDeviceIndex = 0;
        var sampleA = sampler.Sample();
        Assert.NotNull(sampleA.TopProcesses);
        Assert.Contains(sampleA.TopProcesses, p => p.Pid == 1234 && p.UtilizationPercent == 85.0);
        Assert.DoesNotContain(sampleA.TopProcesses, p => p.Pid == 5678);

        // Sample Adapter B: must ONLY include Process 5678, NOT 1234
        sampler.SelectedDeviceIndex = 1;
        var sampleB = sampler.Sample();
        Assert.NotNull(sampleB.TopProcesses);
        Assert.Contains(sampleB.TopProcesses, p => p.Pid == 5678 && p.UtilizationPercent == 95.0);
        Assert.DoesNotContain(sampleB.TopProcesses, p => p.Pid == 1234);
    }

    [Fact]
    public void GpuSampler_DiscoveryRefreshTiming_RespectsCooldown()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_1_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_1_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(10.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        // Call Sample 5 times rapidly
        for (int i = 0; i < 5; i++)
        {
            sampler.Sample();
        }

        // Must query provider instances only once due to cooldown
        Assert.Equal(1, provider.EngineQueryCount);

        // Force refresh bypasses cooldown
        sampler.RefreshCounters(force: true);
        Assert.Equal(2, provider.EngineQueryCount);
    }

    [Fact]
    public void GpuSampler_DriverCounterFailures_HandledGracefullyWithoutCrash()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter
        {
            ValueFactory = () => throw new InvalidOperationException("Counter instance closed")
        };

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        // Should not throw exception; failed counter returns null
        var sample = sampler.Sample();
        Assert.NotNull(sample);
        Assert.Null(sample.Engine3DPercent);
    }

    [Fact]
    public void GpuSampler_NoFabricatedMetrics_WhenCountersZeroOrAbsent()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterB }, counterProvider: provider);

        var sample = sampler.Sample();
        Assert.Null(sample.Engine3DPercent);
        Assert.Null(sample.EngineComputePercent);
        Assert.Null(sample.TemperatureC);
        Assert.Null(sample.PowerWatts);
        Assert.Null(sample.CoreClockMhz);
        Assert.Null(sample.MemoryClockMhz);
    }

    [Fact]
    public async Task GpuSampler_NvidiaStreamingSession_RemainsIndependentAndUnaffected()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(55.0);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<string?>();
        var wrapper = new GpuProcessLifecycleTests.MockProcessWrapper
        {
            OnReadLineAsync = async ct => await channel.Reader.ReadAsync(ct)
        };
        var starter = new GpuProcessLifecycleTests.MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        using var sampler = new GpuTelemetrySampler(
            nvidiaProcessRunner: runner,
            devices: new[] { AdapterA },
            counterProvider: provider);

        // Sample triggers both counter discovery and NVIDIA session start
        channel.Writer.TryWrite("68.0, 52.0, 1700, 7200, 4096");
        sampler.Sample();

        for (int i = 0; i < 50 && sampler.Sample().TemperatureC != 68.0; i++)
        {
            await Task.Delay(10);
        }

        var sample = sampler.Sample();

        // Counter provider metrics
        Assert.Equal(55.0, sample.Engine3DPercent);
        Assert.Equal(55.0, sample.UtilizationPercent);

        // NVIDIA streaming session metrics
        Assert.Equal(68.0, sample.TemperatureC);
        Assert.Equal(52.0, sample.PowerWatts);
        Assert.Equal(1700, sample.CoreClockMhz);
        Assert.Equal(7200, sample.MemoryClockMhz);
    }

    [Fact]
    public void DxgiAdapterResolver_DiscoversRealAdaptersWithLuids_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var adapters = DxgiAdapterResolver.Instance.DiscoverAdapters();
        Assert.NotEmpty(adapters);
        foreach (var adapter in adapters)
        {
            Assert.NotEmpty(adapter.Name);
            Assert.StartsWith("0x", adapter.Luid);
            Assert.Contains("_0x", adapter.Luid);
        }
    }

    [Fact]
    public void GpuSampler_ExactLuidMatching_AcrossMultipleGpus()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        var resolver = new MockGpuAdapterResolver();

        // 2 real GPUs from DXGI
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00010001", 24L * 1024 * 1024 * 1024, 0x10de, 0x2684, false));
        resolver.Adapters.Add(new GpuAdapterIdentity("Intel(R) UHD Graphics 770", "0x00000000_0x00020002", 1024L * 1024 * 1024, 0x8086, 0x4690, false));

        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(60.0);

        provider.EngineInstances.Add("pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(15.0);

        var devNvidia = new GpuDeviceInfo("NVIDIA GeForce RTX 4090", "RTX 4090", 24L * 1024 * 1024 * 1024, "v537.13", "GA102", true, 450.0, 2520, 10500, "Direct3D 12.2");
        var devIntel = new GpuDeviceInfo("Intel(R) UHD Graphics 770", "Intel UHD", 1024L * 1024 * 1024, "v31.0.101.4575", "Intel Gen12", false, 35.0, 1550, 3200, "Direct3D 12.1");

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { devNvidia, devIntel },
            counterProvider: provider,
            adapterResolver: resolver);

        // NVIDIA matches 0x10001 exactly via DXGI
        sampler.SelectedDeviceIndex = 0;
        var sampleNvidia = sampler.Sample();
        Assert.Equal(60.0, sampleNvidia.Engine3DPercent);
        Assert.Equal(60.0, sampleNvidia.UtilizationPercent);

        // Intel matches 0x20002 exactly via DXGI
        sampler.SelectedDeviceIndex = 1;
        var sampleIntel = sampler.Sample();
        Assert.Equal(15.0, sampleIntel.Engine3DPercent);
        Assert.Equal(15.0, sampleIntel.UtilizationPercent);
    }

    [Fact]
    public void GpuSampler_SimilarMemoryGpus_NeverIncorrectlyAssociated()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        var resolver = new MockGpuAdapterResolver();

        // Two completely different GPUs that happen to have the EXACT SAME VRAM (24 GB)
        const long vram24Gb = 24L * 1024 * 1024 * 1024;
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00010001", vram24Gb, 0x10de, 0x2684, false));
        resolver.Adapters.Add(new GpuAdapterIdentity("AMD Radeon RX 7900 XTX", "0x00000000_0x00020002", vram24Gb, 0x1002, 0x744c, false));

        // RTX 4090 is running hard (90% 3D)
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(90.0);

        // RX 7900 XTX is idle (0% 3D)
        provider.EngineInstances.Add("pid_20_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_20_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(0.0);

        var devNvidia = new GpuDeviceInfo("NVIDIA GeForce RTX 4090", "RTX 4090", vram24Gb, "v537.13", "GA102", true, 450.0, 2520, 10500, "Direct3D 12.2");
        var devAmd = new GpuDeviceInfo("AMD Radeon RX 7900 XTX", "RX 7900", vram24Gb, "v23.10", "Navi 31", true, 355.0, 2500, 2500, "Direct3D 12.2");

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { devNvidia, devAmd },
            counterProvider: provider,
            adapterResolver: resolver);

        // Memory heuristic would confuse them; DXGI LUID exact matching ensures AMD never gets NVIDIA's 90%
        sampler.SelectedDeviceIndex = 0;
        Assert.Equal(90.0, sampler.Sample().Engine3DPercent);

        sampler.SelectedDeviceIndex = 1;
        Assert.Equal(0.0, sampler.Sample().Engine3DPercent);
    }

    [Fact]
    public void GpuSampler_EnumerationOrderChanges_DoNotChangeIdentity()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        var resolver = new MockGpuAdapterResolver();

        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00010001", 24L * 1024 * 1024 * 1024, 0x10de, 0x2684, false));
        resolver.Adapters.Add(new GpuAdapterIdentity("Intel(R) UHD Graphics 770", "0x00000000_0x00020002", 1024L * 1024 * 1024, 0x8086, 0x4690, false));

        // Provider lists Intel first, NVIDIA second
        provider.EngineInstances.Add("pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(22.0);

        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(77.0);

        var devNvidia = new GpuDeviceInfo("NVIDIA GeForce RTX 4090", "RTX 4090", 24L * 1024 * 1024 * 1024, "v537.13", "GA102", true, 450.0, 2520, 10500, "Direct3D 12.2");
        var devIntel = new GpuDeviceInfo("Intel(R) UHD Graphics 770", "Intel UHD", 1024L * 1024 * 1024, "v31.0.101.4575", "Intel Gen12", false, 35.0, 1550, 3200, "Direct3D 12.1");

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { devNvidia, devIntel },
            counterProvider: provider,
            adapterResolver: resolver);

        sampler.SelectedDeviceIndex = 0;
        Assert.Equal(77.0, sampler.Sample().Engine3DPercent);

        sampler.SelectedDeviceIndex = 1;
        Assert.Equal(22.0, sampler.Sample().Engine3DPercent);

        // Now reverse resolver DXGI order as well
        resolver.Adapters.Reverse();
        sampler.RefreshCounters(force: true);

        sampler.SelectedDeviceIndex = 0;
        Assert.Equal(77.0, sampler.Sample().Engine3DPercent);

        sampler.SelectedDeviceIndex = 1;
        Assert.Equal(22.0, sampler.Sample().Engine3DPercent);
    }

    [Fact]
    public void GpuSampler_UnresolvedAdapterIdentity_ReturnsUnavailableNullMetrics()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        var resolver = new MockGpuAdapterResolver(); // Empty DXGI adapters

        provider.EngineInstances.Add("pid_1_luid_0x00000000_0x00099999_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_1_luid_0x00000000_0x00099999_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(50.0);

        // Unknown device with no LUID and no match in DXGI
        var unknownDevice = new GpuDeviceInfo(
            Name: "Generic Unknown Virtual Display Adapter",
            ShortName: "Unknown",
            DedicatedBytes: 0,
            DriverVersion: "v1.0",
            VideoProcessor: "VGA",
            IsDiscrete: false,
            PowerTargetWatts: 0,
            BoostClockMhz: 0,
            MemoryClockMhz: 0,
            DirectXLevel: "Direct3D 11.0",
            Luid: null);

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { unknownDevice },
            counterProvider: provider,
            adapterResolver: resolver);

        var sample = sampler.Sample();

        // Must produce null unavailable metrics instead of guessing or defaulting to 0 or another adapter's counters
        Assert.Null(sample.UtilizationPercent);
        Assert.Null(sample.Engine3DPercent);
        Assert.Null(sample.EngineComputePercent);
        Assert.Null(sample.DedicatedMemoryUsedBytes);
        Assert.Null(sample.SharedMemoryUsedBytes);
        Assert.Equal("GPU Performance Counters Unavailable", sample.Status);
        Assert.Equal("Adapter Identity Unresolved", sample.ThrottleStatus);
        Assert.NotNull(sample.TopProcesses);
        Assert.Empty(sample.TopProcesses);
    }

    [Fact]
    public void GpuSampler_ValidZeroCounterValues_DisplayZeroPercent()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(0.0);

        provider.MemoryInstances.Add("luid_0x00000000_0x00010001_phys_0");
        provider.MemoryCounters["luid_0x00000000_0x00010001_phys_0#Dedicated Usage"] = new MockGpuCounter(0.0);

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        var sample = sampler.Sample();

        // Genuine 0 reading must be 0.0, NOT null and NOT fabricated
        Assert.Equal(0.0, sample.Engine3DPercent);
        Assert.Equal(0.0, sample.UtilizationPercent);
        Assert.Equal(0L, sample.DedicatedMemoryUsedBytes);

        var vm = new GpuPanelViewModel();
        vm.ApplyMetrics(sample);

        Assert.Equal("0.0%", vm.UtilizationDisplay);
        Assert.Equal("0.0%", vm.Engine3DDisplay);
        Assert.StartsWith("0.0 /", vm.DedicatedVramDisplay);
    }

    [Fact]
    public void GpuSampler_MissingOrFailedCounters_DisplayNA_RatherThanZero()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        // Counter read fails with exception
        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter
        {
            ValueFactory = () => throw new InvalidOperationException("Failed to read counter")
        };

        using var sampler = new GpuTelemetrySampler(devices: new[] { AdapterA }, counterProvider: provider);

        var sample = sampler.Sample();

        // Missing or failed counter must produce null, NOT 0.0
        Assert.Null(sample.Engine3DPercent);
        Assert.Null(sample.UtilizationPercent);
        Assert.Null(sample.DedicatedMemoryUsedBytes);

        var vm = new GpuPanelViewModel();
        vm.ApplyMetrics(sample);

        Assert.Equal("N/A", vm.UtilizationDisplay);
        Assert.Equal("N/A", vm.Engine3DDisplay);
        Assert.Equal("N/A", vm.DedicatedVramDisplay);
    }

    [Fact]
    public void GpuSampler_AdapterRemovalAndReEnumeration_InvalidatesStaleMappings()
    {
        var provider = new MockGpuPerformanceCounterProvider();
        var resolver = new MockGpuAdapterResolver();

        // Initially adapter has LUID 0x10001
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00010001", 24L * 1024 * 1024 * 1024, 0x10de, 0x2684, false));

        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_10_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(60.0);

        var dev = new GpuDeviceInfo("NVIDIA GeForce RTX 4090", "RTX 4090", 24L * 1024 * 1024 * 1024, "v537.13", "GA102", true, 450.0, 2520, 10500, "Direct3D 12.2");

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { dev },
            counterProvider: provider,
            adapterResolver: resolver);

        var sample1 = sampler.Sample();
        Assert.Equal(60.0, sample1.Engine3DPercent);

        // Simulate adapter disconnect & re-enumeration with new LUID 0x30003
        resolver.Adapters.Clear();
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00030003", 24L * 1024 * 1024 * 1024, 0x10de, 0x2684, false));

        provider.EngineInstances.Clear();
        provider.EngineInstances.Add("pid_10_luid_0x00000000_0x00030003_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_10_luid_0x00000000_0x00030003_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(45.0);

        sampler.RefreshCounters(force: true);

        var sample2 = sampler.Sample();

        // Sampler must not stick to stale 0x10001 mapping; it must resolve to new 0x30003
        Assert.Equal(45.0, sample2.Engine3DPercent);
    }

    [Fact]
    public void GpuSampler_IdenticalAdapterNamesWithDifferentLuids_SegregatedAccurately()
    {
        var provider = new MockGpuPerformanceCounterProvider();

        // Two physical adapters sharing identical names (e.g. SLI or multi-GPU workstation)
        const long vram24Gb = 24L * 1024 * 1024 * 1024;
        var gpu1 = new GpuDeviceInfo(
            Name: "NVIDIA GeForce RTX 4090",
            ShortName: "RTX 4090 #1",
            DedicatedBytes: vram24Gb,
            DriverVersion: "v537.13",
            VideoProcessor: "GA102",
            IsDiscrete: true,
            PowerTargetWatts: 450.0,
            BoostClockMhz: 2520,
            MemoryClockMhz: 10500,
            DirectXLevel: "Direct3D 12.2",
            Luid: "0x00000000_0x00010001");

        var gpu2 = new GpuDeviceInfo(
            Name: "NVIDIA GeForce RTX 4090",
            ShortName: "RTX 4090 #2",
            DedicatedBytes: vram24Gb,
            DriverVersion: "v537.13",
            VideoProcessor: "GA102",
            IsDiscrete: true,
            PowerTargetWatts: 450.0,
            BoostClockMhz: 2520,
            MemoryClockMhz: 10500,
            DirectXLevel: "Direct3D 12.2",
            Luid: "0x00000000_0x00020002");

        // Counters for GPU 1 (LUID 0x10001): 85% 3D
        provider.EngineInstances.Add("pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_100_luid_0x00000000_0x00010001_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(85.0);

        // Counters for GPU 2 (LUID 0x20002): 12% 3D
        provider.EngineInstances.Add("pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D");
        provider.EngineCounters["pid_200_luid_0x00000000_0x00020002_phys_0_eng_0_engtype_3D"] = new MockGpuCounter(12.0);

        using var sampler = new GpuTelemetrySampler(
            devices: new[] { gpu1, gpu2 },
            counterProvider: provider);

        // Sample GPU 1: must report 85%
        sampler.SelectedDeviceIndex = 0;
        var sample1 = sampler.Sample();
        Assert.Equal(85.0, sample1.Engine3DPercent);

        // Sample GPU 2: must report 12% without cross-contamination or name-collision
        sampler.SelectedDeviceIndex = 1;
        var sample2 = sampler.Sample();
        Assert.Equal(12.0, sample2.Engine3DPercent);

        // Sub-case: Two adapters with identical names without attached LUIDs where DXGI has duplicate names
        var resolver = new MockGpuAdapterResolver();
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00010001", vram24Gb, 0x10de, 0x2684, false));
        resolver.Adapters.Add(new GpuAdapterIdentity("NVIDIA GeForce RTX 4090", "0x00000000_0x00020002", vram24Gb, 0x10de, 0x2684, false));

        var unattachedGpu = new GpuDeviceInfo("NVIDIA GeForce RTX 4090", "RTX 4090", vram24Gb, "v537.13", "GA102", true, 450.0, 2520, 10500, "Direct3D 12.2", Luid: null);
        using var ambiguousSampler = new GpuTelemetrySampler(
            devices: new[] { unattachedGpu },
            counterProvider: provider,
            adapterResolver: resolver);

        // Must refuse to guess between identical names and return null unavailable metrics
        var ambiguousSample = ambiguousSampler.Sample();
        Assert.Null(ambiguousSample.Engine3DPercent);
        Assert.Null(ambiguousSample.UtilizationPercent);
    }

    [Fact]
    public void DxgiAdapterResolver_RepeatedEnumeration_ReleasesComResourcesWithoutLeak()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Perform 50 consecutive enumerations to ensure COM objects (IDXGIFactory, IDXGIAdapter)
        // and unmanaged pointers are cleanly released without reference leaks or memory corruption.
        for (int i = 0; i < 50; i++)
        {
            var adapters = DxgiAdapterResolver.Instance.DiscoverAdapters();
            Assert.NotNull(adapters);
            foreach (var adapter in adapters)
            {
                Assert.NotEmpty(adapter.Name);
                Assert.StartsWith("0x", adapter.Luid);
            }
        }
    }
}
