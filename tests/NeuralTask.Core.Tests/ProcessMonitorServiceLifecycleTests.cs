using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessMonitorServiceLifecycleTests
{
    private class FakeSampler : IProcessDataSampler
    {
        public Func<IReadOnlyList<RawProcessSample>>? OnCollect { get; set; }

        public IReadOnlyList<RawProcessSample> CollectRawSamples()
        {
            return OnCollect != null ? OnCollect() : Array.Empty<RawProcessSample>();
        }
    }

    [Fact]
    public async Task NormalShutdown_StopsMonitoringLoopAndCleansUp()
    {
        var sampler = new FakeSampler();
        using var service = new ProcessMonitorService(sampler);
        service.RefreshInterval = TimeSpan.FromMilliseconds(50);

        await service.StartAsync();
        Assert.True(service.IsRunning);

        await Task.Delay(100);

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task CancellationDuringSampling_PropagatesCancellationCorrectly()
    {
        var sampler = new FakeSampler();
        var enterEvent = new ManualResetEventSlim(false);
        var releaseEvent = new ManualResetEventSlim(false);

        sampler.OnCollect = () =>
        {
            enterEvent.Set();
            releaseEvent.Wait(TimeSpan.FromSeconds(2));
            return Array.Empty<RawProcessSample>();
        };

        using var service = new ProcessMonitorService(sampler);
        service.RefreshInterval = TimeSpan.FromMilliseconds(20);

        await service.StartAsync();

        // Wait until sampler is actively in CollectRawSamples
        enterEvent.Wait(TimeSpan.FromSeconds(2));

        // Call StopAsync with a cancellation token that times out quickly
        using var fastCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.StopAsync(fastCts.Token);
        });

        // Release the blocked sampler so it cleanly exits
        releaseEvent.Set();

        // Now call StopAsync without timeout token to ensure clean finalization
        await service.StopAsync();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task BlockedSamplingOperation_AccountsForLifetimeWithoutDisposingDependenciesWhileActive()
    {
        var sampler = new FakeSampler();
        var samplingStarted = new ManualResetEventSlim(false);
        var unblockSampling = new ManualResetEventSlim(false);

        sampler.OnCollect = () =>
        {
            samplingStarted.Set();
            unblockSampling.Wait(TimeSpan.FromSeconds(3));
            return new List<RawProcessSample>
            {
                new(100, "sample.exe", 0, 100, 100, 1000, 0, 0, 0, DateTimeOffset.UtcNow, null, false)
            };
        };

        var service = new ProcessMonitorService(sampler);
        service.RefreshInterval = TimeSpan.FromMilliseconds(10);

        await service.StartAsync();

        // Wait until sampling loop enters CollectRawSamples
        samplingStarted.Wait(TimeSpan.FromSeconds(2));

        // Dispose service asynchronously while sampling is blocked in native code
        var disposeTask = Task.Run(async () =>
        {
            await service.DisposeAsync();
        });

        // Wait a short moment to ensure dispose has set _isDisposed and isolated the active sampling task
        await Task.Delay(100);

        // Unblock the sampling operation
        unblockSampling.Set();

        // Await disposal to ensure no ObjectDisposedException was thrown
        await disposeTask;

        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task CallerCancellation_WhileSamplingRemainsBlocked_PreservesTaskOwnershipAndCanBeAwaitedLater()
    {
        var sampler = new FakeSampler();
        var samplingEntered = new ManualResetEventSlim(false);
        var unblockSampling = new ManualResetEventSlim(false);

        sampler.OnCollect = () =>
        {
            samplingEntered.Set();
            unblockSampling.Wait(TimeSpan.FromSeconds(5));
            return Array.Empty<RawProcessSample>();
        };

        var service = new ProcessMonitorService(sampler);
        service.RefreshInterval = TimeSpan.FromMilliseconds(10);
        await service.StartAsync();

        // Wait until sampling loop enters CollectRawSamples
        samplingEntered.Wait(TimeSpan.FromSeconds(2));

        // Caller 1 calls StopAsync with a 30ms timeout token -> cancels caller wait
        using var fastCts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.StopAsync(fastCts1.Token);
        });

        // Verification: Task ownership is NOT lost; state remains Stopping
        Assert.Equal(ProcessMonitorService.ServiceLifecycleState.Stopping, service.LifecycleState);

        // Caller 2 also attempts StopAsync with another fast timeout -> also cancels caller wait because task is still active
        using var fastCts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.StopAsync(fastCts2.Token);
        });

        Assert.Equal(ProcessMonitorService.ServiceLifecycleState.Stopping, service.LifecycleState);

        // Now release the blocked sampler
        unblockSampling.Set();

        // Caller 3 calls StopAsync with ample timeout -> successfully awaits completion of the stopping operation
        using var successCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StopAsync(successCts.Token);

        Assert.Equal(ProcessMonitorService.ServiceLifecycleState.Stopped, service.LifecycleState);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task SamplingOperation_AttemptingToStartDuringShutdown_ReturnsCachedSnapshotWithoutAcquiringLock()
    {
        int collectCalls = 0;
        var sampler = new FakeSampler
        {
            OnCollect = () =>
            {
                Interlocked.Increment(ref collectCalls);
                return Array.Empty<RawProcessSample>();
            }
        };

        var service = new ProcessMonitorService(sampler);
        await service.StartAsync();
        await service.StopAsync();

        Assert.Equal(ProcessMonitorService.ServiceLifecycleState.Stopped, service.LifecycleState);
        int callsBefore = Volatile.Read(ref collectCalls);

        // Calling SampleOnce while stopped must reject new sampling and return current cached snapshot
        var snap = service.SampleOnce();

        Assert.NotNull(snap);
        Assert.Equal(callsBefore, Volatile.Read(ref collectCalls)); // No new sampling pass initiated!
    }

    [Fact]
    public async Task RepeatedShutdown_IsSafeAndIdempotent()
    {
        var sampler = new FakeSampler();
        using var service = new ProcessMonitorService(sampler);
        service.RefreshInterval = TimeSpan.FromMilliseconds(50);

        await service.StartAsync();
        Assert.True(service.IsRunning);

        // First Stop
        await service.StopAsync();
        Assert.False(service.IsRunning);

        // Second Stop
        await service.StopAsync();
        Assert.False(service.IsRunning);

        // Concurrent stops
        var t1 = service.StopAsync();
        var t2 = service.StopAsync();
        await Task.WhenAll(t1, t2);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task NoResourceAccessAfterDisposal_HandlesOperationsSafely()
    {
        var sampler = new FakeSampler();
        var service = new ProcessMonitorService(sampler);
        await service.StartAsync();
        await service.DisposeAsync();

        // SampleOnce after disposal returns current cached snapshot safely without throwing
        var snapshot = service.SampleOnce();
        Assert.NotNull(snapshot);

        // StartAsync after disposal throws ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await service.StartAsync();
        });

        // StopAsync after disposal returns cleanly
        await service.StopAsync();
    }
}
