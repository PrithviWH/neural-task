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

    [Fact]
    public void GpuTelemetrySampler_WhenHardwareThermalsUnsupported_ReturnsNullAndDoesNotFabricate()
    {
        // When running on standard test environment or without NVIDIA hardware sensors
        using var sampler = new GpuTelemetrySampler();
        var sample = sampler.Sample();

        Assert.NotNull(sample);
        // Verify no synthetic mathematical formulas (e.g., 45.0 + util * 0.2, 18.0 + util * 0.2)
        // If the adapter is non-NVIDIA or sensors not present, temperature and power must be null
        if (!sample.AdapterName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Null(sample.TemperatureC);
            Assert.Null(sample.PowerWatts);
            Assert.Null(sample.HotspotTemperatureC);
            Assert.Null(sample.CoreClockMhz);
            Assert.Null(sample.MemoryClockMhz);
            Assert.Null(sample.FanSpeedRpm);
        }
    }

    [Fact]
    public void GpuTelemetrySampler_TopProcesses_DoesNotContainPidDerivedVramOrInjectedDummies()
    {
        using var sampler = new GpuTelemetrySampler();
        var sample = sampler.Sample();

        Assert.NotNull(sample);
        if (sample.TopProcesses != null && sample.TopProcesses.Count > 0)
        {
            foreach (var p in sample.TopProcesses)
            {
                // Verify no fake formula 120.0 + (p.Pid % 180) exists
                if (p.MemoryMb.HasValue)
                {
                    Assert.NotEqual(Math.Round(120.0 + (p.Pid % 180), 0), p.MemoryMb.Value);
                }
                else
                {
                    Assert.Null(p.MemoryMb);
                }

                // Verify normalized process names have no duplicate .exe suffixes
                Assert.DoesNotContain(".exe.exe", p.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void GpuPanelViewModel_WhenMetricsAreNullOrUnsupported_DisplaysNAAndUnavailableCorrectly()
    {
        var vm = new GpuPanelViewModel();

        var metrics = new GpuMetrics(
            AdapterName: "Intel Iris Xe Graphics",
            UtilizationPercent: 15.0,
            DedicatedMemoryUsedBytes: 0,
            DedicatedMemoryTotalBytes: 0,
            SharedMemoryUsedBytes: 0,
            Engine3DPercent: 15.0,
            EngineVideoDecodePercent: 0.0,
            EngineCopyPercent: 0.0,
            EngineComputePercent: 0.0,
            Timestamp: DateTime.UtcNow,
            TemperatureC: null,
            HotspotTemperatureC: null,
            PowerWatts: null,
            CoreClockMhz: null,
            MemoryClockMhz: null,
            FanSpeedRpm: null,
            FanProfile: null,
            TopProcesses: new[]
            {
                new GpuProcessItem(1234, "test_render", "3D Raster", 15.0, "DirectX", null)
            }
        );

        vm.ApplyMetrics(metrics);

        Assert.Equal("N/A", vm.TemperatureDisplay);
        Assert.Equal("N/A", vm.HotspotDisplay);
        Assert.Equal("N/A", vm.PowerDisplay);
        Assert.Equal("N/A", vm.CoreClockDisplay);
        Assert.Equal("N/A", vm.MemoryClockDisplay);
        Assert.Equal("N/A", vm.FanRpmDisplay);
        Assert.Equal("N/A", vm.FanProfile);
        Assert.Equal("N/A", vm.TemperatureStatus);
        Assert.Equal(0.0, vm.PowerPercent);

        Assert.Single(vm.TopProcesses);
        Assert.Equal("—", vm.TopProcesses[0].MemoryDisplay);
    }

    [Fact]
    public void GpuPanelViewModel_WhenGenuineMetricsProvided_DisplaysAccurateFormattedValues()
    {
        var vm = new GpuPanelViewModel();

        var metrics = new GpuMetrics(
            AdapterName: "NVIDIA GeForce RTX 4080",
            UtilizationPercent: 75.0,
            DedicatedMemoryUsedBytes: 8L * 1024 * 1024 * 1024,
            DedicatedMemoryTotalBytes: 16L * 1024 * 1024 * 1024,
            SharedMemoryUsedBytes: 512L * 1024 * 1024,
            Engine3DPercent: 75.0,
            EngineVideoDecodePercent: 0.0,
            EngineCopyPercent: 2.0,
            EngineComputePercent: 10.0,
            Timestamp: DateTime.UtcNow,
            TemperatureC: 72.4,
            HotspotTemperatureC: 77.1,
            PowerWatts: 65.0,
            PowerTargetWatts: 120.0,
            CoreClockMhz: 1850,
            BoostClockMhz: 2200,
            MemoryClockMhz: 7000,
            MemoryClockMaxMhz: 7500,
            FanSpeedRpm: 1800,
            FanProfile: "Active Dual Fans",
            TopProcesses: new[]
            {
                new GpuProcessItem(2200, "blender.exe", "3D Raster", 75.0, "DirectX", 2048.0)
            }
        );

        vm.ApplyMetrics(metrics);

        Assert.Equal("72°C", vm.TemperatureDisplay);
        Assert.Equal("77.1°C", vm.HotspotDisplay);
        Assert.Equal("65 W", vm.PowerDisplay);
        Assert.Equal("1,850 MHz", vm.CoreClockDisplay);
        Assert.Equal("7,000 MHz", vm.MemoryClockDisplay);
        Assert.Equal("1,800 RPM", vm.FanRpmDisplay);
        Assert.Equal("Active Dual Fans", vm.FanProfile);
        Assert.Equal("Warm", vm.TemperatureStatus);

        Assert.Single(vm.TopProcesses);
        Assert.Equal("2,048 MB", vm.TopProcesses[0].MemoryDisplay);
    }

    [Fact]
    public void GpuPanelViewModel_HotspotDisplay_SupportsReadAndWrite_ToPreventWpfTwoWayBindingExceptions()
    {
        var vm = new GpuPanelViewModel();
        var prop = typeof(GpuPanelViewModel).GetProperty(nameof(GpuPanelViewModel.HotspotDisplay));
        Assert.NotNull(prop);
        Assert.True(prop.CanRead);
        Assert.True(prop.CanWrite, "HotspotDisplay must have a setter to prevent WPF TwoWay binding crashes on Run.Text");

        // Verify setter does not throw and maintains display logic
        prop.SetValue(vm, "ignored");
        Assert.Equal("N/A", vm.HotspotDisplay);

        vm.HotspotTemperatureC = 85.5;
        Assert.Equal("85.5°C", vm.HotspotDisplay);
    }

    [Fact]
    public void GpuTelemetrySampler_DiscoversDevices_PrioritizesDiscreteGpu()
    {
        using var sampler = new GpuTelemetrySampler();
        var sample = sampler.Sample();
        Assert.NotNull(sample);
        Assert.NotEmpty(sample.Devices);

        // When a discrete GPU is present (e.g. RTX 3060 Laptop GPU), it must be ordered first and selected
        var firstDevice = sample.Devices[0];
        Assert.True(firstDevice.IsSelected);
        if (sample.Devices.Any(d => d.IsDiscrete))
        {
            Assert.True(firstDevice.IsDiscrete);
            Assert.Equal(firstDevice.Name, sample.AdapterName);
        }
    }

    [Fact]
    public void GpuPanelViewModel_SelectDeviceCommand_TriggersDeviceSelectionRequestedAndTogglesSelection()
    {
        var vm = new GpuPanelViewModel();

        var metrics = new GpuMetrics(
            AdapterName: "NVIDIA GeForce RTX 3060 Laptop GPU",
            UtilizationPercent: 15.0,
            DedicatedMemoryUsedBytes: 1024L * 1024 * 1024,
            DedicatedMemoryTotalBytes: 6144L * 1024 * 1024,
            SharedMemoryUsedBytes: 256L * 1024 * 1024,
            Engine3DPercent: 15.0,
            EngineVideoDecodePercent: 0.0,
            EngineCopyPercent: 1.0,
            EngineComputePercent: 5.0,
            Timestamp: DateTime.UtcNow,
            AvailableDevices: new[]
            {
                new GpuDeviceItem(0, "NVIDIA GeForce RTX 3060 Laptop GPU", "RTX 3060 Laptop", 6144L * 1024 * 1024, "v32.0.16.2124", true, true),
                new GpuDeviceItem(1, "AMD Radeon(TM) Graphics", "AMD Radeon", 512L * 1024 * 1024, "v31.0.21926.1001", false, false)
            },
            SelectedDeviceIndex: 0);

        vm.ApplyMetrics(metrics);
        Assert.Equal(2, vm.AvailableDevices.Count);
        Assert.True(vm.AvailableDevices[0].IsSelected);
        Assert.False(vm.AvailableDevices[1].IsSelected);

        int requestedIndex = -1;
        vm.DeviceSelectionRequested += idx => requestedIndex = idx;

        // Execute command to select AMD iGPU (index 1)
        Assert.True(vm.SelectDeviceCommand.CanExecute(1));
        vm.SelectDeviceCommand.Execute(1);

        Assert.Equal(1, requestedIndex);
        Assert.False(vm.AvailableDevices[0].IsSelected);
        Assert.True(vm.AvailableDevices[1].IsSelected);
    }

    [Fact]
    public void GpuDeviceItemViewModel_RaisesPropertyChangedEvents_WhenSelectionChanges()
    {
        var item = new GpuDeviceItemViewModel
        {
            Index = 0,
            Name = "NVIDIA GeForce RTX 3060 Laptop GPU",
            ShortName = "RTX 3060 Laptop",
            IsDiscrete = true,
            IsSelected = false
        };

        var changedProps = new System.Collections.Generic.List<string>();
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        item.IsSelected = true;

        Assert.Contains(nameof(GpuDeviceItemViewModel.IsSelected), changedProps);
        Assert.Contains(nameof(GpuDeviceItemViewModel.PillBackground), changedProps);
        Assert.Contains(nameof(GpuDeviceItemViewModel.PillBorderBrush), changedProps);
        Assert.Contains(nameof(GpuDeviceItemViewModel.PillForeground), changedProps);
    }

    [Fact]
    public void GpuPanelViewModel_VoltageAndThresholds_CalculatedAccurately()
    {
        var vm = new GpuPanelViewModel();

        var metrics = new GpuMetrics(
            AdapterName: "NVIDIA GeForce RTX 3060 Laptop GPU",
            UtilizationPercent: 92.0,
            DedicatedMemoryUsedBytes: 2048L * 1024 * 1024,
            DedicatedMemoryTotalBytes: 6144L * 1024 * 1024,
            SharedMemoryUsedBytes: 128L * 1024 * 1024,
            Engine3DPercent: 92.0,
            EngineVideoDecodePercent: 0.0,
            EngineCopyPercent: 1.0,
            EngineComputePercent: 5.0,
            Timestamp: DateTime.UtcNow,
            TemperatureC: 86.5,
            PowerWatts: 78.0,
            VoltageV: 0.985);

        vm.ApplyMetrics(metrics);

        Assert.Equal("0.985 V", vm.VoltageDisplay);
        Assert.Equal("#EF4444", vm.TemperatureThresholdColor); // Red at >= 85C
        Assert.Equal("#EF4444", vm.UtilizationThresholdColor); // Red at >= 90%
    }
}
