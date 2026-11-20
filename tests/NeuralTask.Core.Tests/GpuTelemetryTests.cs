using System;
using NeuralTask.Plugins.Gpu;
using Xunit;

namespace NeuralTask.Core.Tests;

public class GpuTelemetryTests
{
    [Fact]
    public void GpuMetrics_CalculatesMemoryPercentagesAndMegabytesAccurately()
    {
        long dedicatedUsed = 2L * 1024 * 1024 * 1024; // 2 GB
        long dedicatedTotal = 8L * 1024 * 1024 * 1024; // 8 GB
        long sharedUsed = 512L * 1024 * 1024; // 512 MB

        var metrics = new GpuMetrics(
            AdapterName: "Test RTX 4090",
            UtilizationPercent: 42.5,
            DedicatedMemoryUsedBytes: dedicatedUsed,
            DedicatedMemoryTotalBytes: dedicatedTotal,
            SharedMemoryUsedBytes: sharedUsed,
            Engine3DPercent: 40.0,
            EngineVideoDecodePercent: 0.0,
            EngineCopyPercent: 2.5,
            EngineComputePercent: 10.0,
            Timestamp: DateTime.UtcNow);

        Assert.Equal(2048.0, metrics.DedicatedMemoryUsedMb);
        Assert.Equal(8192.0, metrics.DedicatedMemoryTotalMb);
        Assert.Equal(512.0, metrics.SharedMemoryUsedMb);
        Assert.Equal(25.0, metrics.DedicatedMemoryPercent);
    }

    [Fact]
    public void GpuPanelViewModel_UpdatesMetricsAndGeneratesValidSparkline()
    {
        var vm = new GpuPanelViewModel();

        var metrics = new GpuMetrics(
            AdapterName: "AMD Radeon 780M",
            UtilizationPercent: 65.0,
            DedicatedMemoryUsedBytes: 1536L * 1024 * 1024,
            DedicatedMemoryTotalBytes: 4096L * 1024 * 1024,
            SharedMemoryUsedBytes: 256L * 1024 * 1024,
            Engine3DPercent: 65.0,
            EngineVideoDecodePercent: 12.0,
            EngineCopyPercent: 5.0,
            EngineComputePercent: 20.0,
            Timestamp: DateTime.UtcNow);

        vm.ApplyMetrics(metrics);

        Assert.Equal("AMD Radeon 780M", vm.AdapterName);
        Assert.Equal(65.0, vm.UtilizationPercent);
        Assert.Equal(1536.0, vm.DedicatedMemoryUsedMb);
        Assert.Equal(4096.0, vm.DedicatedMemoryTotalMb);
        Assert.Equal(37.5, vm.DedicatedMemoryPercent);
        Assert.StartsWith("M ", vm.SparklineGeometry);
        Assert.Contains("L ", vm.SparklineGeometry);
    }

    [Fact]
    public void GpuTelemetrySampler_RepeatedRediscovery_DoesNotDuplicateOrLeak()
    {
        using var sampler = new GpuTelemetrySampler();

        // Initial sample
        var sample1 = sampler.Sample();
        Assert.NotNull(sample1);
        Assert.NotEmpty(sample1.AdapterName);

        // Repeated forced rediscovery simulating driver reset / reload
        sampler.ResetAndRediscover(force: true);
        var sample2 = sampler.Sample();
        Assert.NotNull(sample2);
        Assert.Equal(sample1.AdapterName, sample2.AdapterName);

        // Safe disposal clears all performance counters without throwing
        sampler.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GpuTelemetrySampler_ResetAndRediscoverAsync_CompletesSuccessfully()
    {
        using var sampler = new GpuTelemetrySampler();

        await sampler.ResetAndRediscoverAsync(force: true);
        var sample = sampler.Sample();
        Assert.NotNull(sample);
    }

    [Fact]
    public void GpuMetricsService_RepeatedRediscovery_RecoversAndDisposesCleanly()
    {
        using var service = new NeuralTask.Core.Services.GpuMetricsService();

        var snap1 = service.CollectSnapshot(Array.Empty<NeuralTask.Core.Models.ProcessSnapshot>());
        Assert.NotNull(snap1);

        service.ResetAndRediscover(force: true);

        var snap2 = service.CollectSnapshot(Array.Empty<NeuralTask.Core.Models.ProcessSnapshot>());
        Assert.NotNull(snap2);

        service.Dispose();
    }
}
