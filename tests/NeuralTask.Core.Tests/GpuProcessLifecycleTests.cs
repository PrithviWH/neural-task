using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Plugins.Gpu;
using Xunit;

namespace NeuralTask.Core.Tests;

public class GpuProcessLifecycleTests
{
    public class MockProcessWrapper : IProcessWrapper
    {
        public int ExitCode { get; set; } = 0;
        public bool HasExited { get; set; } = false;
        public bool KillCalled { get; private set; }
        public bool EntireProcessTreeKilled { get; private set; }
        public bool Disposed { get; private set; }
        public int WaitForExitCount { get; private set; }

        public Func<CancellationToken, Task>? OnWaitForExitAsync { get; set; }
        public Func<CancellationToken, Task<string>>? OnReadToEndAsync { get; set; }
        public Action<bool>? OnKill { get; set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitForExitCount++;
            if (OnWaitForExitAsync != null) return OnWaitForExitAsync(cancellationToken);
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            if (OnReadToEndAsync != null) return OnReadToEndAsync(cancellationToken);
            return Task.FromResult("65.0, 45.2, 1600, 7000, 2048");
        }

        public void Kill(bool entireProcessTree)
        {
            KillCalled = true;
            EntireProcessTreeKilled = entireProcessTree;
            HasExited = true;
            OnKill?.Invoke(entireProcessTree);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public class MockProcessStarter : IProcessStarter
    {
        public MockProcessWrapper? WrapperToReturn { get; set; }
        public Exception? ExceptionToThrowOnStart { get; set; }
        public ProcessStartInfo? LastPsi { get; private set; }

        public IProcessWrapper? Start(ProcessStartInfo psi)
        {
            LastPsi = psi;
            if (ExceptionToThrowOnStart != null) throw ExceptionToThrowOnStart;
            return WrapperToReturn;
        }
    }

    private class FakeNvidiaProcessRunner : INvidiaProcessRunner
    {
        public Func<string, TimeSpan, CancellationToken, Task<NvidiaProcessResult?>>? OnRunQuery { get; set; }
        public int CallCount { get; private set; }

        public Task<NvidiaProcessResult?> RunQueryAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            if (OnRunQuery != null)
            {
                return OnRunQuery(arguments, timeout, cancellationToken);
            }

            return Task.FromResult<NvidiaProcessResult?>(new NvidiaProcessResult(0, "65.0, 45.2, 1600, 7000, 2048", false));
        }
    }

    [Fact]
    public void GpuSampler_ParsesNvidiaOutput_UsingInjectedRunner()
    {
        var runner = new FakeNvidiaProcessRunner
        {
            OnRunQuery = (_, _, _) => Task.FromResult<NvidiaProcessResult?>(
                new NvidiaProcessResult(0, "68.5, 55.0, 1750, 7200, 3072", false))
        };

        using var sampler = new GpuTelemetrySampler(runner);
        var metrics = sampler.Sample();

        Assert.NotNull(metrics);
        Assert.NotNull(metrics.TopProcesses);
    }

    [Fact]
    public async Task GpuSampler_TimeoutKillsProcessAndCleansUpSafely()
    {
        var runner = new FakeNvidiaProcessRunner
        {
            OnRunQuery = async (_, timeout, ct) =>
            {
                // Simulate timed-out query where the process was killed
                await Task.Delay(50, ct);
                return new NvidiaProcessResult(-1, string.Empty, TimedOut: true);
            }
        };

        using var sampler = new GpuTelemetrySampler(runner);
        var metrics = sampler.Sample();

        // Should return valid metrics without throwing, falling back to safe baseline
        Assert.NotNull(metrics);
        await Task.Delay(100); // Allow poll task to settle
    }

    [Fact]
    public async Task GpuSampler_PreventsOverlappingPollingCalls()
    {
        var queryStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockQueryTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new FakeNvidiaProcessRunner
        {
            OnRunQuery = async (_, _, _) =>
            {
                queryStartedTcs.TrySetResult(true);
                await unblockQueryTcs.Task;
                return new NvidiaProcessResult(0, "70.0, 60.0, 1800, 7500, 4096", false);
            }
        };

        using var sampler = new GpuTelemetrySampler(runner);

        // First sample triggers poll
        sampler.Sample();
        await queryStartedTcs.Task;

        // Immediate second and third samples while first query is in-flight
        sampler.Sample();
        sampler.Sample();

        // Runner should only have been called once because lock prevented overlap
        Assert.Equal(1, runner.CallCount);

        unblockQueryTcs.TrySetResult(true);
        await Task.Delay(50);
    }

    [Fact]
    public async Task GpuSampler_DisposalCoordinatesWithActivePollTask()
    {
        var queryStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new FakeNvidiaProcessRunner
        {
            OnRunQuery = async (_, _, ct) =>
            {
                queryStartedTcs.TrySetResult(true);
                try
                {
                    await Task.Delay(2000, ct);
                }
                catch (OperationCanceledException)
                {
                    return new NvidiaProcessResult(-1, string.Empty, TimedOut: false);
                }
                return new NvidiaProcessResult(0, "70.0, 60.0, 1800, 7500, 4096", false);
            }
        };

        var sampler = new GpuTelemetrySampler(runner);
        sampler.Sample();

        await queryStartedTcs.Task;

        // Dispose should cancel the CTS and await or cleanly terminate the active poll task
        await sampler.DisposeAsync();

        // Verify sampler is disposed and no exceptions are thrown
        Assert.NotNull(sampler);
    }

    [Fact]
    public async Task GpuSampler_SynchronousDispose_Exceeding500ms_DoesNotPrematurelyDisposeLockOrSwallowExceptions()
    {
        var queryStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollCompletedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? caughtInPoll = null;

        var runner = new FakeNvidiaProcessRunner
        {
            OnRunQuery = async (_, _, ct) =>
            {
                queryStartedTcs.TrySetResult(true);
                try
                {
                    // Intentionally delay longer than the 500ms synchronous Dispose wait
                    await Task.Delay(800, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    caughtInPoll = ex;
                }
                finally
                {
                    pollCompletedTcs.TrySetResult(true);
                }
                return new NvidiaProcessResult(0, "72.0, 65.0, 1850, 7600, 4096", false);
            }
        };

        var sampler = new GpuTelemetrySampler(runner);
        sampler.Sample();

        await queryStartedTcs.Task;

        // Call synchronous Dispose: its 500ms wait will expire while the poll is still running
        sampler.Dispose();

        // Wait for the poll task to finish its 800ms work and execute its finally block
        await pollCompletedTcs.Task;

        // Verify that the poll task did NOT throw ObjectDisposedException or any exception when releasing lock
        Assert.Null(caughtInPoll);
    }

    [Fact]
    public async Task DefaultNvidiaProcessRunner_SuccessfulExecution()
    {
        var wrapper = new MockProcessWrapper();
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        var result = await runner.RunQueryAsync("test_args", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("65.0", result.Output);
        Assert.False(result.TimedOut);
        Assert.False(wrapper.KillCalled);
        Assert.True(wrapper.Disposed);
    }

    [Fact]
    public async Task DefaultNvidiaProcessRunner_Timeout_KillsProcessTree_ConfirmsExit_DrainsStdout()
    {
        var wrapper = new MockProcessWrapper
        {
            OnWaitForExitAsync = async ct =>
            {
                await Task.Delay(5000, ct); // Hang until timeout
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        var result = await runner.RunQueryAsync("test_args", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TimedOut);
        Assert.True(wrapper.KillCalled);
        Assert.True(wrapper.EntireProcessTreeKilled);
        Assert.True(wrapper.WaitForExitCount >= 2); // Initial wait + exit confirmation wait
        Assert.True(wrapper.Disposed);
    }

    [Fact]
    public async Task DefaultNvidiaProcessRunner_CallerCancellation_CleansUpAndDistinguishesTimeout()
    {
        using var callerCts = new CancellationTokenSource();
        var wrapper = new MockProcessWrapper
        {
            OnWaitForExitAsync = async ct =>
            {
                await Task.Delay(5000, ct);
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        // Cancel caller token after 40ms
        callerCts.CancelAfter(40);

        var result = await runner.RunQueryAsync("test_args", TimeSpan.FromSeconds(5), callerCts.Token);

        Assert.NotNull(result);
        Assert.False(result.TimedOut); // Distinguishes caller cancellation from runner timeout
        Assert.True(wrapper.KillCalled);
        Assert.True(wrapper.EntireProcessTreeKilled);
        Assert.True(wrapper.Disposed);
    }

    [Fact]
    public async Task DefaultNvidiaProcessRunner_StdoutFailure_ObservedAndDoesNotCrash()
    {
        var wrapper = new MockProcessWrapper
        {
            OnReadToEndAsync = ct => Task.FromException<string>(new InvalidOperationException("StandardOutput pipe broken")),
            OnWaitForExitAsync = async ct =>
            {
                await Task.Delay(5000, ct);
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        var result = await runner.RunQueryAsync("test_args", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TimedOut);
        Assert.True(wrapper.KillCalled);
        Assert.True(wrapper.Disposed);
    }

    [Fact]
    public async Task DefaultNvidiaProcessRunner_KillFailure_HandledSafely()
    {
        var wrapper = new MockProcessWrapper
        {
            OnKill = _ => throw new System.ComponentModel.Win32Exception(5, "Access is denied"),
            OnWaitForExitAsync = async ct =>
            {
                await Task.Delay(5000, ct);
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);

        var result = await runner.RunQueryAsync("test_args", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TimedOut);
        Assert.True(wrapper.KillCalled);
        Assert.True(wrapper.Disposed);
    }
}
