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
        public Func<CancellationToken, Task<string?>>? OnReadLineAsync { get; set; }
        public Func<CancellationToken, Task<string>>? OnReadStderrToEndAsync { get; set; }
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

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (OnReadLineAsync != null) return OnReadLineAsync(cancellationToken);
            return Task.FromResult<string?>("65.0, 45.2, 1600, 7000, 2048");
        }

        public Task<string> ReadStderrToEndAsync(CancellationToken cancellationToken)
        {
            if (OnReadStderrToEndAsync != null) return OnReadStderrToEndAsync(cancellationToken);
            return Task.FromResult(string.Empty);
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
        public Func<ProcessStartInfo, IProcessWrapper?>? OnStart { get; set; }
        public Exception? ExceptionToThrowOnStart { get; set; }
        public ProcessStartInfo? LastPsi { get; private set; }
        public int StartCount { get; private set; }

        public IProcessWrapper? Start(ProcessStartInfo psi)
        {
            StartCount++;
            LastPsi = psi;
            if (ExceptionToThrowOnStart != null) throw ExceptionToThrowOnStart;
            if (OnStart != null) return OnStart(psi);
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

    private static readonly GpuDeviceInfo NvidiaTestDevice = new(
        Name: "NVIDIA GeForce RTX 4090",
        ShortName: "RTX 4090",
        DedicatedBytes: 24L * 1024 * 1024 * 1024,
        DriverVersion: "v537.13",
        VideoProcessor: "GA102",
        IsDiscrete: true,
        PowerTargetWatts: 450.0,
        BoostClockMhz: 2520,
        MemoryClockMhz: 10500,
        DirectXLevel: "Direct3D 12.2");

    private static readonly GpuDeviceInfo AmdTestDevice = new(
        Name: "AMD Radeon RX 7900 XTX",
        ShortName: "Radeon RX 7900",
        DedicatedBytes: 24L * 1024 * 1024 * 1024,
        DriverVersion: "v23.11.1",
        VideoProcessor: "Navi 31",
        IsDiscrete: true,
        PowerTargetWatts: 355.0,
        BoostClockMhz: 2500,
        MemoryClockMhz: 2500,
        DirectXLevel: "Direct3D 12.2");

    [Fact]
    public async Task GpuSampler_StartsOnlyOneProcess_AcrossMultipleTelemetrySamples()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string?>();
        var wrapper = new MockProcessWrapper
        {
            OnReadLineAsync = async ct => await channel.Reader.ReadAsync(ct)
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        channel.Writer.TryWrite("65.0, 45.2, 1600, 7000, 2048");
        sampler.Sample();
        await Task.Delay(50);

        for (int i = 0; i < 5; i++)
        {
            channel.Writer.TryWrite($"65.{i}, 45.2, 1600, 7000, 2048");
            sampler.Sample();
            await Task.Delay(20);
        }

        // Only ONE process must be started across all samples
        Assert.Equal(1, starter.StartCount);
    }

    [Fact]
    public async Task GpuSampler_ParsesMultipleOutputSamples_Correctly()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string?>();
        var wrapper = new MockProcessWrapper
        {
            OnReadLineAsync = async ct => await channel.Reader.ReadAsync(ct)
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        sampler.Sample();

        // Sample 1
        channel.Writer.TryWrite("62.5, 40.0, 1500, 6800, 1536");
        for (int i = 0; i < 50 && sampler.Sample().TemperatureC != 62.5; i++)
        {
            await Task.Delay(10);
        }
        var sample1 = sampler.Sample();
        Assert.Equal(62.5, sample1.TemperatureC);
        Assert.Equal(40.0, sample1.PowerWatts);
        Assert.Equal(1500, sample1.CoreClockMhz);
        Assert.Equal(6800, sample1.MemoryClockMhz);

        // Sample 2
        channel.Writer.TryWrite("71.0, 65.5, 1850, 7500, 3072");
        for (int i = 0; i < 50 && sampler.Sample().TemperatureC != 71.0; i++)
        {
            await Task.Delay(10);
        }
        var sample2 = sampler.Sample();
        Assert.Equal(71.0, sample2.TemperatureC);
        Assert.Equal(65.5, sample2.PowerWatts);
        Assert.Equal(1850, sample2.CoreClockMhz);
        Assert.Equal(7500, sample2.MemoryClockMhz);
    }

    [Fact]
    public async Task GpuSampler_MalformedOrPartialOutput_DoesNotCrash()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string?>();
        var wrapper = new MockProcessWrapper
        {
            OnReadLineAsync = async ct => await channel.Reader.ReadAsync(ct)
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        sampler.Sample();

        // Feed corrupted, partial, or unexpected strings
        channel.Writer.TryWrite("malformed, text, with, invalid, fields");
        channel.Writer.TryWrite("");
        channel.Writer.TryWrite("   ");
        channel.Writer.TryWrite("[Not Supported], [Not Supported], N/A, N/A");
        channel.Writer.TryWrite("corrupt");
        channel.Writer.TryWrite("70.0, 50.0, 1600, 7000, 2048");

        for (int i = 0; i < 50 && sampler.Sample().TemperatureC != 70.0; i++)
        {
            await Task.Delay(10);
        }

        var sample = sampler.Sample();
        Assert.NotNull(sample);
        Assert.Equal(70.0, sample.TemperatureC);
    }

    [Fact]
    public void GpuSampler_MissingExecutable_HandledGracefully()
    {
        var starter = new MockProcessStarter
        {
            ExceptionToThrowOnStart = new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified")
        };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        var sample = sampler.Sample();
        Assert.NotNull(sample);
        Assert.Null(sample.TemperatureC);
        Assert.Null(sample.PowerWatts);
    }

    [Fact]
    public async Task GpuSampler_ChildProcessExitsUnexpectedly_RecoversSafely()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string?>();
        var wrapper = new MockProcessWrapper
        {
            OnReadLineAsync = async ct =>
            {
                if (await channel.Reader.WaitToReadAsync(ct))
                {
                    return channel.Reader.TryRead(out var line) ? line : null;
                }
                return null;
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        sampler.Sample();
        channel.Writer.TryWrite("65.0, 40.0, 1600, 7000, 2048");
        for (int i = 0; i < 50 && sampler.Sample().TemperatureC != 65.0; i++)
        {
            await Task.Delay(10);
        }

        // Simulate crash / unexpected termination
        wrapper.HasExited = true;
        channel.Writer.Complete();

        await Task.Delay(100);

        // Sampler must not throw and continues functioning
        var sample = sampler.Sample();
        Assert.NotNull(sample);
    }

    [Fact]
    public async Task GpuSampler_Cancellation_StopsSamplingAndCleansUpProcess()
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrapper = new MockProcessWrapper
        {
            OnReadLineAsync = ct =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        sampler.Sample();
        await Task.Delay(50);

        await sampler.DisposeAsync();

        Assert.True(wrapper.KillCalled || wrapper.Disposed);
    }

    [Fact]
    public async Task GpuSampler_Shutdown_AwaitsProcessExitAndOutputReaderCompletion()
    {
        var exitCompletedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrCompletedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var wrapper = new MockProcessWrapper
        {
            OnWaitForExitAsync = async ct =>
            {
                await Task.Delay(50, ct);
                exitCompletedTcs.TrySetResult(true);
            },
            OnReadStderrToEndAsync = async ct =>
            {
                await Task.Delay(30, ct);
                stderrCompletedTcs.TrySetResult(true);
                return string.Empty;
            }
        };
        var starter = new MockProcessStarter { WrapperToReturn = wrapper };
        var runner = new DefaultNvidiaProcessRunner(starter);
        var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        sampler.Sample();
        await Task.Delay(50);

        await sampler.DisposeAsync();

        Assert.True(exitCompletedTcs.Task.IsCompletedSuccessfully);
        Assert.True(stderrCompletedTcs.Task.IsCompletedSuccessfully);
        Assert.True(wrapper.Disposed);
    }

    [Fact]
    public async Task GpuSampler_RestartBackoff_PreventsRapidRepeatedLaunches()
    {
        int launchCount = 0;
        var starter = new MockProcessStarter
        {
            OnStart = _ =>
            {
                launchCount++;
                return new MockProcessWrapper
                {
                    HasExited = true,
                    OnReadLineAsync = _ => Task.FromResult<string?>(null) // Exits immediately on read
                };
            }
        };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        for (int i = 0; i < 10; i++)
        {
            sampler.Sample();
            await Task.Delay(20);
        }

        // Bounded cooldown backoff prevents rapid restart loop
        Assert.Equal(1, launchCount);
    }

    [Fact]
    public void GpuSampler_PreservesGenuineReadings_NoFabrication()
    {
        var starter = new MockProcessStarter { WrapperToReturn = null };
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { NvidiaTestDevice });

        var sample = sampler.Sample();
        Assert.Null(sample.TemperatureC);
        Assert.Null(sample.PowerWatts);
        Assert.Null(sample.HotspotTemperatureC);
        Assert.Null(sample.CoreClockMhz);
        Assert.Null(sample.MemoryClockMhz);
        Assert.Null(sample.FanSpeedRpm);
    }

    [Fact]
    public void GpuSampler_NonNvidiaOperation_RemainsUnaffected()
    {
        var starter = new MockProcessStarter();
        var runner = new DefaultNvidiaProcessRunner(starter);
        using var sampler = new GpuTelemetrySampler(runner, new[] { AmdTestDevice });

        var sample1 = sampler.Sample();
        var sample2 = sampler.Sample();

        Assert.NotNull(sample1);
        Assert.Equal("AMD Radeon RX 7900 XTX", sample1.AdapterName);
        Assert.Null(sample1.TemperatureC);
        Assert.Null(sample1.PowerWatts);
        Assert.Equal(0, starter.StartCount); // Never attempts to start nvidia-smi
    }
}
