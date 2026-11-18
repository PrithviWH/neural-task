using System;
using System.Collections.Generic;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessMonitorServiceTests
{
    private class FakeTimeSource : ITimeSource
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public long TimestampTicks { get; set; } = 1000;
        public double TicksPerSecond { get; set; } = 1000.0; // 1000 ticks = 1 second
    }

    private class FakeSampler : IProcessDataSampler
    {
        public List<RawProcessSample> SamplesToReturn { get; set; } = new();

        public IReadOnlyList<RawProcessSample> CollectRawSamples() => SamplesToReturn;
    }

    [Fact]
    public void DeltaCalculations_ComputeAccurateCpuDiskAndNetMetrics()
    {
        // Arrange
        var timeSource = new FakeTimeSource();
        var sampler = new FakeSampler();
        const int processorCount = 4; // 4 logical cores

        using var service = new ProcessMonitorService(sampler, timeSource, processorCount);

        // Sample 1 at t = 1000 ticks
        timeSource.TimestampTicks = 1000;
        sampler.SamplesToReturn = new List<RawProcessSample>
        {
            new(
                Pid: 101,
                Name: "testproc.exe",
                ParentPid: 1,
                KernelTimeTicks: 10_000_000,  // 1.0s
                UserTimeTicks: 10_000_000,    // 1.0s (Total = 2.0s)
                WorkingSetBytes: 50_000_000,
                ReadTransferBytes: 1000,
                WriteTransferBytes: 2000,     // Total Disk = 3000
                OtherTransferBytes: 500,      // Total Net = 500
                StartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
                ExecutablePath: @"C:\Test\testproc.exe",
                IsAccessDenied: false)
        };

        var firstSnapshot = service.SampleOnce();
        Assert.Single(firstSnapshot);
        // First sample has no baseline, so rates must be 0
        Assert.Equal(0.0, firstSnapshot[0].CpuPercent);
        Assert.Equal(0.0, firstSnapshot[0].DiskBytesPerSec);
        Assert.Equal(0.0, firstSnapshot[0].NetworkBytesPerSec);

        // Sample 2 at t = 2000 ticks (exactly 1.0 second elapsed)
        timeSource.TimestampTicks = 2000;
        sampler.SamplesToReturn = new List<RawProcessSample>
        {
            new(
                Pid: 101,
                Name: "testproc.exe",
                ParentPid: 1,
                KernelTimeTicks: 20_000_000,  // +1.0s
                UserTimeTicks: 20_000_000,    // +1.0s (Total consumed = 2.0s CPU time)
                WorkingSetBytes: 55_000_000,
                ReadTransferBytes: 6000,      // +5000
                WriteTransferBytes: 7000,     // +5000 (Total delta disk = 10,000 bytes)
                OtherTransferBytes: 2500,     // +2000 (Total delta net = 2,000 bytes)
                StartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
                ExecutablePath: @"C:\Test\testproc.exe",
                IsAccessDenied: false)
        };

        var secondSnapshot = service.SampleOnce();
        Assert.Single(secondSnapshot);

        var proc = secondSnapshot[0];
        // CPU calculation: 2.0s CPU consumed across 4 cores in 1.0s wall time: (2.0 / (1.0 * 4)) * 100 = 50.0%
        Assert.Equal(50.0, proc.CpuPercent);
        // Disk calculation: 10,000 bytes / 1.0s = 10,000 bytes/sec
        Assert.Equal(10000.0, proc.DiskBytesPerSec);
        // Network calculation: 2,000 bytes / 1.0s = 2,000 bytes/sec
        Assert.Equal(2000.0, proc.NetworkBytesPerSec);
        Assert.Equal(55_000_000, proc.RamBytes);
        Assert.False(proc.IsAccessDenied);
    }

    [Fact]
    public void AccessDeniedProcesses_AreFlaggedWithoutThrowing()
    {
        // Arrange
        var timeSource = new FakeTimeSource();
        var sampler = new FakeSampler();
        using var service = new ProcessMonitorService(sampler, timeSource);

        sampler.SamplesToReturn = new List<RawProcessSample>
        {
            new(
                Pid: 4,
                Name: "System",
                ParentPid: 0,
                KernelTimeTicks: 0,
                UserTimeTicks: 0,
                WorkingSetBytes: 1024,
                ReadTransferBytes: 0,
                WriteTransferBytes: 0,
                OtherTransferBytes: 0,
                StartTime: null,
                ExecutablePath: null,
                IsAccessDenied: true)
        };

        // Act
        var snapshots = service.SampleOnce();

        // Assert
        Assert.Single(snapshots);
        var systemProc = snapshots[0];
        Assert.True(systemProc.IsAccessDenied);
        Assert.Equal(4, systemProc.Pid);
        Assert.Equal("System", systemProc.Name);
        Assert.Equal(0.0, systemProc.CpuPercent);
        Assert.Null(systemProc.ExecutablePath);
        Assert.Null(systemProc.StartTime);
    }

    [Fact]
    public void ReactiveStreamAndEvent_EmitUpdatedSnapshots()
    {
        // Arrange
        var timeSource = new FakeTimeSource();
        var sampler = new FakeSampler();
        using var service = new ProcessMonitorService(sampler, timeSource);

        IReadOnlyList<ProcessSnapshot>? eventReceived = null;
        IReadOnlyList<ProcessSnapshot>? observableReceived = null;

        service.SnapshotsUpdated += (_, list) => eventReceived = list;
        using var subscription = service.Snapshots.Subscribe(new SimpleObserver(list => observableReceived = list));

        sampler.SamplesToReturn = new List<RawProcessSample>
        {
            new(10, "app.exe", 1, 0, 0, 1000, 0, 0, 0, null, null, false)
        };

        // Act
        service.SampleOnce();

        // Assert
        Assert.NotNull(eventReceived);
        Assert.NotNull(observableReceived);
        Assert.Same(eventReceived, observableReceived);
        Assert.Single(eventReceived);
    }

    private class SimpleObserver : IObserver<IReadOnlyList<ProcessSnapshot>>
    {
        private readonly Action<IReadOnlyList<ProcessSnapshot>> _onNext;
        public SimpleObserver(Action<IReadOnlyList<ProcessSnapshot>> onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(IReadOnlyList<ProcessSnapshot> value) => _onNext(value);
    }
}
