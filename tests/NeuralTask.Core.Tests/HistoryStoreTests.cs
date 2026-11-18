using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class HistoryStoreTests
{
    private static SystemMetricsSnapshot CreateDummySystemMetrics(double cpu = 15.0, double ramPercent = 50.0)
    {
        return new SystemMetricsSnapshot(
            CpuPercent: cpu,
            TotalMemoryBytes: 16_000_000_000,
            UsedMemoryBytes: 8_000_000_000,
            AvailableMemoryBytes: 8_000_000_000,
            MemoryPercent: ramPercent,
            ProcessIoBytesPerSec: 1024 * 1024,
            NetworkReceivedBytesPerSec: 50 * 1024,
            NetworkSentBytesPerSec: 25 * 1024,
            ProcessorName: "Test Processor",
            LogicalCoreCount: 8,
            OsVersion: "Windows 11",
            ProcessCount: 100,
            Uptime: TimeSpan.FromHours(5)
        );
    }

    private static IReadOnlyList<ProcessSnapshot> CreateDummyProcesses(int count = 20)
    {
        var list = new List<ProcessSnapshot>();
        for (int i = 1; i <= count; i++)
        {
            list.Add(new ProcessSnapshot(
                Pid: 1000 + i,
                Name: $"process_{i}.exe",
                ParentPid: 1000,
                CpuPercent: i * 2.5,
                RamBytes: i * 10_000_000,
                DiskBytesPerSec: i * 5000,
                NetworkBytesPerSec: i * 1000,
                StartTime: DateTime.Now.AddHours(-1),
                ExecutablePath: $@"C:\Program Files\process_{i}.exe",
                IsAccessDenied: false
            ));
        }
        return list;
    }

    [Fact]
    public void AddSample_StoresSample_AndEnforcesRetentionWindow()
    {
        var store = new HistoryStore(defaultRetention: TimeSpan.FromMinutes(10));
        var baseTime = DateTimeOffset.UtcNow;

        // Add 15 samples spaced 1 minute apart
        for (int i = 0; i < 15; i++)
        {
            var time = baseTime.AddMinutes(i);
            store.AddSample(time, CreateDummySystemMetrics(cpu: i * 5.0), CreateDummyProcesses());
        }

        // Only the last 11 samples (from minute 4/5 to 14) should be within the 10-minute window
        Assert.True(store.SampleCount <= 11);
        Assert.True(store.SampleCount >= 10);

        var oldest = store.GetHistory().First();
        var newest = store.GetHistory().Last();
        Assert.True((newest.Timestamp - oldest.Timestamp) <= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddSample_CapsTopOffendersPerSample()
    {
        var store = new HistoryStore();
        store.AddSample(DateTimeOffset.UtcNow, CreateDummySystemMetrics(), CreateDummyProcesses(50));

        var latest = store.GetLatest();
        Assert.NotNull(latest);
        // Capped at top 10 CPU + top 10 RAM (deduplicated: max 20)
        Assert.True(latest.TopProcesses.Count <= 20);
        Assert.True(latest.TopProcesses.Count >= 10);
    }

    [Fact]
    public void GetSampleAt_ReturnsClosestSampleViaBinarySearch()
    {
        var store = new HistoryStore();
        var t0 = DateTimeOffset.UtcNow;

        store.AddSample(t0, CreateDummySystemMetrics(cpu: 10.0), CreateDummyProcesses());
        store.AddSample(t0.AddSeconds(10), CreateDummySystemMetrics(cpu: 20.0), CreateDummyProcesses());
        store.AddSample(t0.AddSeconds(20), CreateDummySystemMetrics(cpu: 30.0), CreateDummyProcesses());
        store.AddSample(t0.AddSeconds(30), CreateDummySystemMetrics(cpu: 40.0), CreateDummyProcesses());

        // Exact match
        var sample10 = store.GetSampleAt(t0.AddSeconds(10));
        Assert.NotNull(sample10);
        Assert.Equal(20.0, sample10.CpuPercent);

        // Near 22s (closer to 20s than 30s)
        var sample22 = store.GetSampleAt(t0.AddSeconds(22));
        Assert.NotNull(sample22);
        Assert.Equal(30.0, sample22.CpuPercent);

        // Beyond range returns boundary
        var before = store.GetSampleAt(t0.AddSeconds(-50));
        Assert.NotNull(before);
        Assert.Equal(10.0, before.CpuPercent);

        var after = store.GetSampleAt(t0.AddSeconds(100));
        Assert.NotNull(after);
        Assert.Equal(40.0, after.CpuPercent);
    }

    [Fact]
    public void GetSampleByFraction_MapsNormalizedPositionCorrectly()
    {
        var store = new HistoryStore();
        var t0 = DateTimeOffset.UtcNow;

        for (int i = 0; i <= 10; i++)
        {
            store.AddSample(t0.AddSeconds(i * 10), CreateDummySystemMetrics(cpu: i * 10.0), CreateDummyProcesses());
        }

        var oldest = store.GetSampleByFraction(0.0);
        Assert.NotNull(oldest);
        Assert.Equal(0.0, oldest.CpuPercent);

        var mid = store.GetSampleByFraction(0.5);
        Assert.NotNull(mid);
        Assert.Equal(50.0, mid.CpuPercent);

        var newest = store.GetSampleByFraction(1.0);
        Assert.NotNull(newest);
        Assert.Equal(100.0, newest.CpuPercent);
    }

    [Fact]
    public void HardMaxCapacity_PreventsUnboundedGrowth()
    {
        var store = new HistoryStore(defaultRetention: TimeSpan.FromHours(10)); // Wide window
        var t0 = DateTimeOffset.UtcNow;

        // Add samples beyond HardMaxCapacity (4000)
        for (int i = 0; i < 4200; i++)
        {
            store.AddSample(t0.AddSeconds(i), CreateDummySystemMetrics(), CreateDummyProcesses(2));
        }

        Assert.Equal(store.MaxSamples, store.SampleCount);
    }

    [Fact]
    public async Task PersistAndLoad_RecoversHistorySuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"neuraltask_history_test_{Guid.NewGuid():N}.json");
        try
        {
            var store = new HistoryStore(persistenceEnabled: true);
            var t0 = DateTimeOffset.UtcNow;

            store.AddSample(t0.AddMinutes(-5), CreateDummySystemMetrics(cpu: 25.0), CreateDummyProcesses(5));
            store.AddSample(t0.AddMinutes(-2), CreateDummySystemMetrics(cpu: 55.0), CreateDummyProcesses(5));

            await store.PersistAsync(tempFile);
            Assert.True(File.Exists(tempFile));

            var store2 = new HistoryStore(persistenceEnabled: true);
            bool loaded = await store2.LoadPersistedAsync(tempFile);

            Assert.True(loaded);
            Assert.Equal(2, store2.SampleCount);
            Assert.Equal(25.0, store2.GetHistory()[0].CpuPercent);
            Assert.Equal(55.0, store2.GetHistory()[1].CpuPercent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPersisted_CorruptFile_ReturnsFalseWithoutThrowing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"neuraltask_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, "{ broken json content !@#$ }");
            var store = new HistoryStore(persistenceEnabled: true);
            bool loaded = await store.LoadPersistedAsync(tempFile);

            Assert.False(loaded);
            Assert.Equal(0, store.SampleCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Clear_EmptiesAllSamples()
    {
        var store = new HistoryStore();
        store.AddSample(DateTimeOffset.UtcNow, CreateDummySystemMetrics(), CreateDummyProcesses());
        Assert.Equal(1, store.SampleCount);

        store.Clear();
        Assert.Equal(0, store.SampleCount);
        Assert.Null(store.GetLatest());
    }
}
