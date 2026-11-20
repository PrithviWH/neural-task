using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralTask.Plugins.Gpu;

public record NvidiaProcessResult(int ExitCode, string Output, bool TimedOut);

public interface INvidiaProcessSession : IAsyncDisposable, IDisposable
{
    bool HasExited { get; }
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    Task StopAsync(TimeSpan timeout);
}

public interface INvidiaProcessRunner
{
    Task<NvidiaProcessResult?> RunQueryAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken);
    INvidiaProcessSession? StartSession(string arguments, CancellationToken cancellationToken) => null;
}

public interface IProcessWrapper : IDisposable
{
    int ExitCode { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task<string> ReadToEndAsync(CancellationToken cancellationToken);
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    Task<string> ReadStderrToEndAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree);
}

public interface IProcessStarter
{
    IProcessWrapper? Start(ProcessStartInfo psi);
}

public class StandardProcessWrapper : IProcessWrapper
{
    private readonly Process _process;
    public StandardProcessWrapper(Process process) => _process = process ?? throw new ArgumentNullException(nameof(process));
    public int ExitCode => _process.ExitCode;
    public bool HasExited => _process.HasExited;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
    public Task<string> ReadToEndAsync(CancellationToken cancellationToken) => _process.StandardOutput.ReadToEndAsync(cancellationToken);
    public Task<string?> ReadLineAsync(CancellationToken cancellationToken) => _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
    public Task<string> ReadStderrToEndAsync(CancellationToken cancellationToken) => _process.StandardError.ReadToEndAsync(cancellationToken);
    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);
    public void Dispose() => _process.Dispose();
}

public class StandardProcessStarter : IProcessStarter
{
    public static readonly StandardProcessStarter Instance = new();
    public IProcessWrapper? Start(ProcessStartInfo psi)
    {
        var proc = Process.Start(psi);
        return proc != null ? new StandardProcessWrapper(proc) : null;
    }
}

public class DefaultNvidiaProcessSession : INvidiaProcessSession
{
    private readonly IProcessWrapper _process;
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly Task<string> _stderrDrainTask;
    private bool _isDisposed;
    private readonly object _lock = new();

    public DefaultNvidiaProcessSession(IProcessWrapper process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        // Drain stderr continuously in the background so the stderr pipe buffer never fills up
        _stderrDrainTask = Task.Run(async () =>
        {
            try
            {
                return await _process.ReadStderrToEndAsync(_sessionCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error draining nvidia-smi stderr: {ex.Message}");
                return string.Empty;
            }
        });
    }

    public bool HasExited => _process.HasExited;

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts.Token);
        try
        {
            return await _process.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _sessionCts.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            _sessionCts.Cancel();
        }
        catch (ObjectDisposedException) { }

        // 1. Terminate process tree if still running
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to kill nvidia-smi process tree: {ex.Message}");
        }

        // 2. Await process exit with bounded timeout
        using var exitCts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning($"nvidia-smi process did not exit within {timeout.TotalMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Error waiting for nvidia-smi process exit: {ex.Message}");
        }

        // 3. Observe and complete stderr drain task
        try
        {
            await _stderrDrainTask.ConfigureAwait(false);
        }
        catch { }

        // 4. Dispose process and CTS
        try
        {
            _process.Dispose();
        }
        catch { }

        try
        {
            _sessionCts.Dispose();
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromMilliseconds(1000)).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        try
        {
            StopAsync(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}

public class DefaultNvidiaProcessRunner : INvidiaProcessRunner
{
    public static readonly DefaultNvidiaProcessRunner Instance = new();
    private readonly IProcessStarter _processStarter;

    public DefaultNvidiaProcessRunner(IProcessStarter? processStarter = null)
    {
        _processStarter = processStarter ?? StandardProcessStarter.Instance;
    }

    public INvidiaProcessSession? StartSession(string arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;

        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        IProcessWrapper? proc = null;
        try
        {
            proc = _processStarter.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to start nvidia-smi session: {ex.Message}");
            return null;
        }

        if (proc == null) return null;

        return new DefaultNvidiaProcessSession(proc);
    }

    public async Task<NvidiaProcessResult?> RunQueryAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;

        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        IProcessWrapper? proc = null;
        try
        {
            proc = _processStarter.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to start nvidia-smi: {ex.Message}");
            return null;
        }

        if (proc == null) return null;

        using (proc)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var readOutputTask = proc.ReadToEndAsync(linkedCts.Token);

            try
            {
                await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                var output = await readOutputTask.ConfigureAwait(false);
                return new NvidiaProcessResult(proc.ExitCode, output, false);
            }
            catch (OperationCanceledException)
            {
                bool isTimeout = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

                // 1. Process termination: On timeout or cancellation, terminate the process tree
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception killEx)
                {
                    Trace.TraceError($"Failed to terminate nvidia-smi process tree: {killEx.Message}");
                }

                // 2. Exit confirmation: After requesting termination, await actual process exit using a bounded policy
                using var exitConfirmCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
                try
                {
                    await proc.WaitForExitAsync(exitConfirmCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Trace.TraceWarning("nvidia-smi process did not confirm exit within 1000ms after kill signal.");
                }
                catch (Exception exitEx)
                {
                    Trace.TraceError($"Error awaiting nvidia-smi process exit confirmation: {exitEx.Message}");
                }

                // 3. Output drain observation: Observe and complete the stdout-reading task on every execution path
                try
                {
                    await readOutputTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when linked token is cancelled
                }
                catch (Exception readEx)
                {
                    Trace.TraceWarning($"nvidia-smi stdout reading completed with error: {readEx.Message}");
                }

                return new NvidiaProcessResult(-1, string.Empty, isTimeout);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Unexpected error during nvidia-smi execution: {ex.Message}");

                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception killEx)
                {
                    Trace.TraceError($"Failed to terminate nvidia-smi process tree: {killEx.Message}");
                }

                using var exitConfirmCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
                try
                {
                    await proc.WaitForExitAsync(exitConfirmCts.Token).ConfigureAwait(false);
                }
                catch { }

                try
                {
                    await readOutputTask.ConfigureAwait(false);
                }
                catch { }

                return null;
            }
        }
    }
}
