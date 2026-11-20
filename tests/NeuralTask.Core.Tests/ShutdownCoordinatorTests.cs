using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.App.Services;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ShutdownCoordinatorTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = new();
        public bool SaveCalled { get; private set; }
        public event EventHandler<AppSettings>? SettingsChanged;

        public void SaveSettings(AppSettings settings)
        {
            SaveCalled = true;
            CurrentSettings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
    }

    private class FakeHistoryStore : IHistoryStore
    {
        public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromMinutes(30);
        public int SampleCount => 0;
        public bool IsPersistenceEnabled { get; set; } = true;
        public bool PersistCalled { get; private set; }
        public DateTimeOffset PersistTime { get; private set; }
        public Func<CancellationToken, Task>? OnPersist { get; set; }

        public async Task PersistAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            PersistCalled = true;
            PersistTime = DateTimeOffset.UtcNow;
            if (OnPersist != null) await OnPersist(cancellationToken);
        }

        public Task<bool> LoadPersistedAsync(string? filePath = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void AddSample(DateTimeOffset timestamp, SystemMetricsSnapshot systemMetrics, IReadOnlyList<ProcessSnapshot> processes, IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null) { }

        public IReadOnlyList<HistoricalSample> GetHistory() => Array.Empty<HistoricalSample>();
        public HistoricalSample? GetSampleAt(DateTimeOffset timestamp) => null;
        public HistoricalSample? GetSampleByFraction(double fraction) => null;
        public HistoricalSample? GetLatest() => null;
        public void Clear() { }
    }

    [Fact]
    public async Task NormalShutdown_ExecutesAllStepsInCorrectOrder()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        bool monitoringStopped = false;
        bool disposed = false;
        bool closeInvoked = false;

        DateTimeOffset stopMonitoringTime = DateTimeOffset.MinValue;
        DateTimeOffset historyPersistTime = DateTimeOffset.MinValue;
        DateTimeOffset disposeTime = DateTimeOffset.MinValue;
        DateTimeOffset closeWindowTime = DateTimeOffset.MinValue;

        historyStore.OnPersist = _ =>
        {
            historyPersistTime = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        };

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: async ct =>
            {
                monitoringStopped = true;
                stopMonitoringTime = DateTimeOffset.UtcNow;
                await Task.Delay(5, ct); // Brief pause to enforce distinct timestamps
            },
            disposeAction: () =>
            {
                disposed = true;
                disposeTime = DateTimeOffset.UtcNow;
                return ValueTask.CompletedTask;
            },
            closeAction: () =>
            {
                closeInvoked = true;
                closeWindowTime = DateTimeOffset.UtcNow;
            });

        Assert.Equal(0, coordinator.ShutdownState);

        bool result = await coordinator.ExecuteShutdownAsync();

        Assert.True(result);
        Assert.Equal(2, coordinator.ShutdownState);
        Assert.True(settings.SaveCalled);
        Assert.True(historyStore.PersistCalled);
        Assert.True(monitoringStopped);
        Assert.True(disposed);
        Assert.True(closeInvoked);

        // Strict ordering assertion: StopMonitoring < FinalHistoryPersistence < DisposeDependencies < CloseWindow
        Assert.True(stopMonitoringTime <= historyPersistTime,
            $"Expected StopMonitoring ({stopMonitoringTime:O}) <= FinalHistoryPersistence ({historyPersistTime:O})");
        Assert.True(historyPersistTime <= disposeTime,
            $"Expected FinalHistoryPersistence ({historyPersistTime:O}) <= DisposeDependencies ({disposeTime:O})");
        Assert.True(disposeTime <= closeWindowTime,
            $"Expected DisposeDependencies ({disposeTime:O}) <= CloseWindow ({closeWindowTime:O})");
    }

    [Fact]
    public async Task MonitoringTimeout_RetainsDependenciesAndDoesNotDisposeWhileMonitoringIsActive()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        bool disposeCalled = false;
        bool closeCalled = false;

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: async ct =>
            {
                // Simulate monitoring operation that remains blocked beyond step timeout
                await Task.Delay(5000, ct);
            },
            disposeAction: () =>
            {
                disposeCalled = true;
                return ValueTask.CompletedTask;
            },
            closeAction: () => closeCalled = true);

        // Execute shutdown with 50ms step timeout
        bool result = await coordinator.ExecuteShutdownAsync(stepTimeout: TimeSpan.FromMilliseconds(50));

        // When monitoring times out:
        // 1. Result reports false (deadline exceeded, unclean shutdown)
        Assert.False(result);
        // 2. Dependencies MUST NOT be disposed to prevent use-after-free
        Assert.False(disposeCalled);
        // 3. Final history persistence MUST NOT be called while monitoring is still alive
        Assert.False(historyStore.PersistCalled);
        // 4. User settings are still safely saved
        Assert.True(settings.SaveCalled);
        // 5. Window close is still invoked so application doesn't hang open permanently
        Assert.True(closeCalled);
        Assert.Equal(2, coordinator.ShutdownState);
    }

    [Fact]
    public async Task DuplicateShutdownCalls_ExecuteExactlyOnce()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        int closeCount = 0;

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: _ => Task.CompletedTask,
            disposeAction: () => ValueTask.CompletedTask,
            closeAction: () => closeCount++);

        var t1 = coordinator.ExecuteShutdownAsync();
        var t2 = coordinator.ExecuteShutdownAsync();

        var results = await Task.WhenAll(t1, t2);

        // Exactly one call should have returned true (the one that transitioned state from 0 to 1)
        Assert.Single(results, r => r);
        Assert.Single(results, r => !r);
        Assert.Equal(1, closeCount);
        Assert.Equal(2, coordinator.ShutdownState);
    }

    [Fact]
    public async Task PersistenceFailure_ContinuesShutdownWithoutHangingOrStayingOpen()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore
        {
            OnPersist = _ => throw new InvalidOperationException("Disk write failure / simulated I/O error")
        };
        bool monitoringStopped = false;
        bool disposed = false;
        bool closeInvoked = false;

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: _ =>
            {
                monitoringStopped = true;
                return Task.CompletedTask;
            },
            disposeAction: () =>
            {
                disposed = true;
                return ValueTask.CompletedTask;
            },
            closeAction: () => closeInvoked = true);

        // Even though persistence threw an exception, shutdown must complete cleanly
        bool result = await coordinator.ExecuteShutdownAsync();

        Assert.True(result);
        Assert.Equal(2, coordinator.ShutdownState);
        Assert.True(monitoringStopped);
        Assert.True(disposed);
        Assert.True(closeInvoked);
    }

    [Fact]
    public async Task TimeoutBehavior_RespectsBoundedStepTimeout()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore
        {
            OnPersist = async ct =>
            {
                // Simulate slow persistence that exceeds step timeout
                await Task.Delay(5000, ct);
            }
        };

        bool closeInvoked = false;
        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: _ => Task.CompletedTask,
            disposeAction: () => ValueTask.CompletedTask,
            closeAction: () => closeInvoked = true);

        // Execute shutdown with 100ms step timeout
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool result = await coordinator.ExecuteShutdownAsync(stepTimeout: TimeSpan.FromMilliseconds(100));
        sw.Stop();

        Assert.True(result);
        Assert.Equal(2, coordinator.ShutdownState);
        Assert.True(closeInvoked);
        // Verify it finished in bounded time (< 1500ms), NOT 5000ms
        Assert.True(sw.ElapsedMilliseconds < 1500);
    }

    [Fact]
    public async Task Cancellation_PropagatesToUnderlyingOperations()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        CancellationToken observedToken = CancellationToken.None;

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: ct =>
            {
                observedToken = ct;
                return Task.CompletedTask;
            },
            disposeAction: () => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        await coordinator.ExecuteShutdownAsync(cancellationToken: cts.Token);

        Assert.True(observedToken.CanBeCanceled);
    }

    [Fact]
    public async Task StartupHistoryWait_AwaitsBeforeSavingHistory()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        var loadTaskTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: _ => Task.CompletedTask,
            disposeAction: () => ValueTask.CompletedTask,
            historyLoadTaskProvider: () => loadTaskTcs.Task);

        var shutdownTask = coordinator.ExecuteShutdownAsync();

        // Release the startup history load task
        loadTaskTcs.TrySetResult(true);

        bool result = await shutdownTask;
        Assert.True(result);
        Assert.True(historyStore.PersistCalled);
    }

    [Fact]
    public async Task RepeatedShutdown_AfterCompletion_ReturnsFalseSafely()
    {
        var settings = new FakeSettingsService();
        var historyStore = new FakeHistoryStore();
        int closeCount = 0;

        var coordinator = new AppShutdownCoordinator(
            settings,
            historyStore,
            stopMonitoringAction: _ => Task.CompletedTask,
            disposeAction: () => ValueTask.CompletedTask,
            closeAction: () => closeCount++);

        bool first = await coordinator.ExecuteShutdownAsync();
        Assert.True(first);
        Assert.Equal(1, closeCount);

        // Subsequent call
        bool second = await coordinator.ExecuteShutdownAsync();
        Assert.False(second);
        Assert.Equal(1, closeCount);
    }
}
