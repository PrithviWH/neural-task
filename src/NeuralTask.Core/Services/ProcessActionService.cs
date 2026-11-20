using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Handles graceful process termination and verified kill-tree operations with start-time validation.
/// Non-blocking asynchronous design prevents UI thread freezes.
/// </summary>
public class ProcessActionService : IProcessActionService
{
    private readonly Func<Process, DateTimeOffset>? _startTimeAccessor;

    public ProcessActionService(Func<Process, DateTimeOffset>? startTimeAccessor = null)
    {
        _startTimeAccessor = startTimeAccessor;
    }

    public ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        return ProcessTreeBuilder.BuildTree(snapshots);
    }

    public Task<ProcessTerminationResult> TerminateProcessAsync(
        int pid,
        string? expectedProcessName = null,
        DateTimeOffset? expectedStartTime = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                expectedProcessName ?? "System Idle",
                ProcessTerminationStatus.AccessDenied,
                "Cannot terminate the System Idle process."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                expectedProcessName ?? "Unknown",
                ProcessTerminationStatus.Cancelled,
                "Process termination was cancelled."));
        }

        // Ordinary UI termination must require reliable process identity
        if (string.IsNullOrWhiteSpace(expectedProcessName) || !expectedStartTime.HasValue)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                expectedProcessName ?? "Unknown",
                ProcessTerminationStatus.RefusedAmbiguousIdentity,
                "Process termination refused: reliable process identity (both process name and start time) must be provided to prevent terminating recycled PIDs."));
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(2000);

        // Offload native Windows process inspection, termination, and wait off the caller's thread
        return Task.Run(async () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ProcessTerminationResult(
                    pid,
                    expectedProcessName ?? "Unknown",
                    ProcessTerminationStatus.Cancelled,
                    "Process termination was cancelled.");
            }

            try
            {
                // Note on Win32 Race Conditions:
                // Windows does not expose an atomic "compare-identity-and-kill" syscall for arbitrary PIDs.
                // However, calling Process.GetProcessById(pid) invokes OpenProcess to acquire a kernel
                // SafeProcessHandle. In Windows kernel semantics, as long as an open handle to the EPROCESS block
                // is held by NeuralTask, the kernel guarantees that PID will NOT be recycled for a new process
                // until all handles are closed. Therefore, validating process identity after handle acquisition
                // eliminates the window for PID recycling races prior to calling process.Kill().
                using var process = Process.GetProcessById(pid);
                var name = process.ProcessName;

                if (process.HasExited)
                {
                    return new ProcessTerminationResult(
                        pid,
                        name,
                        ProcessTerminationStatus.AlreadyExited,
                        $"Process (PID: {pid}) has already exited.");
                }

                // 1. Process Name validation
                if (!string.IsNullOrWhiteSpace(expectedProcessName) && !MatchesProcessName(name, expectedProcessName))
                {
                    return new ProcessTerminationResult(
                        pid,
                        name,
                        ProcessTerminationStatus.IdentityMismatch,
                        $"Identity mismatch detected: Process PID {pid} is named '{name}', but expected '{expectedProcessName}'. Termination aborted for safety.");
                }

                // 2. Start-time validation: prevent PID recycling hazards
                if (expectedStartTime.HasValue)
                {
                    try
                    {
                        var actualStart = _startTimeAccessor != null
                            ? _startTimeAccessor(process)
                            : new DateTimeOffset(process.StartTime);
                        if (Math.Abs((actualStart - expectedStartTime.Value).TotalSeconds) > 2.0)
                        {
                            return new ProcessTerminationResult(
                                pid,
                                name,
                                ProcessTerminationStatus.IdentityMismatch,
                                $"PID recycling detected: Process {name} (PID: {pid}) was recycled (started at {actualStart:HH:mm:ss}, expected {expectedStartTime.Value:HH:mm:ss}). Termination aborted for safety.");
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // When QueryInformation is denied (e.g. for protected system processes or elevation boundaries),
                        // start time cannot be verified. To adhere to strict process termination safety rules against
                        // terminating same-name recycled processes, termination must be safely refused.
                        return new ProcessTerminationResult(
                            pid,
                            name,
                            ProcessTerminationStatus.RefusedAmbiguousIdentity,
                            $"Process termination refused: unable to verify start time for {name} (PID: {pid}) due to insufficient permissions (Access Denied). Refusing termination to prevent ambiguous kill of recycled or protected processes.");
                    }
                    catch (InvalidOperationException)
                    {
                        return new ProcessTerminationResult(
                            pid,
                            name,
                            ProcessTerminationStatus.AlreadyExited,
                            $"Process (PID: {pid}) has already exited.");
                    }
                }

                process.Kill();

                int timeoutMs = Math.Max(100, (int)effectiveTimeout.TotalMilliseconds);
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new ProcessTerminationResult(
                            pid,
                            name,
                            ProcessTerminationStatus.Cancelled,
                            "Process termination was cancelled.");
                    }

                    return new ProcessTerminationResult(
                        pid,
                        name,
                        ProcessTerminationStatus.Failed,
                        $"Process {name} (PID: {pid}) did not exit within {timeoutMs}ms.");
                }

                return new ProcessTerminationResult(
                    pid,
                    name,
                    ProcessTerminationStatus.Success,
                    $"Successfully terminated {name} (PID: {pid}).");
            }
            catch (ArgumentException)
            {
                return new ProcessTerminationResult(
                    pid,
                    expectedProcessName ?? "Unknown",
                    ProcessTerminationStatus.AlreadyExited,
                    $"Process (PID: {pid}) has already exited.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
            {
                return new ProcessTerminationResult(
                    pid,
                    expectedProcessName ?? "Protected System Process",
                    ProcessTerminationStatus.AccessDenied,
                    $"Access denied. Process (PID: {pid}) is protected or requires elevated Administrator privileges.");
            }
            catch (InvalidOperationException)
            {
                return new ProcessTerminationResult(
                    pid,
                    expectedProcessName ?? "Unknown",
                    ProcessTerminationStatus.AlreadyExited,
                    $"Process (PID: {pid}) has already exited.");
            }
            catch (Exception ex)
            {
                return new ProcessTerminationResult(
                    pid,
                    expectedProcessName ?? "Unknown",
                    ProcessTerminationStatus.Failed,
                    $"Termination failed for PID {pid}: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static bool MatchesProcessName(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        var a = actual.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? actual[..^4] : actual;
        var e = expected.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? expected[..^4] : expected;
        return string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<TreeTerminationResult> TerminateTreeAsync(
        int rootPid,
        IReadOnlyList<ProcessSnapshot> currentSnapshots,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new TreeTerminationResult(
                rootPid,
                new[] { new ProcessTerminationResult(rootPid, "Unknown", ProcessTerminationStatus.Cancelled, "Tree termination was cancelled.") },
                false);
        }

        // Offload tree construction and traversal to background task to avoid UI hitching
        List<int> pidsToTerminate;
        try
        {
            pidsToTerminate = await Task.Run(() =>
            {
                var tree = BuildTree(currentSnapshots);
                var descendantPids = tree.GetDescendantPids(rootPid);

                // Collect all target PIDs starting from deepest leaves first, then up to the root process
                // to avoid leaves being orphaned or re-parented midway through termination.
                var list = new List<int>(descendantPids);
                list.Reverse();
                list.Add(rootPid);
                return list;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new TreeTerminationResult(
                rootPid,
                new[] { new ProcessTerminationResult(rootPid, "Unknown", ProcessTerminationStatus.Cancelled, "Tree termination was cancelled.") },
                false);
        }

        var results = new List<ProcessTerminationResult>(pidsToTerminate.Count);
        bool allSuccessful = true;
        var snapshotMap = currentSnapshots?.ToDictionary(s => s.Pid) ?? new Dictionary<int, ProcessSnapshot>();

        foreach (var pid in pidsToTerminate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new ProcessTerminationResult(
                    pid,
                    snapshotMap.TryGetValue(pid, out var cancelSnap) ? cancelSnap.Name : "Unknown",
                    ProcessTerminationStatus.Cancelled,
                    "Tree termination was cancelled."));
                allSuccessful = false;
                break;
            }

            snapshotMap.TryGetValue(pid, out var snap);
            if (snap == null || string.IsNullOrWhiteSpace(snap.Name) || !snap.StartTime.HasValue)
            {
                results.Add(new ProcessTerminationResult(
                    pid,
                    snap?.Name ?? "Unknown",
                    ProcessTerminationStatus.RefusedAmbiguousIdentity,
                    $"Descendant process (PID: {pid}) lacks verifiable identity in snapshot. Termination refused."));
                allSuccessful = false;
                continue;
            }

            var res = await TerminateProcessAsync(
                pid,
                expectedProcessName: snap.Name,
                expectedStartTime: snap.StartTime,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            results.Add(res);
            if (res.Status != ProcessTerminationStatus.Success && res.Status != ProcessTerminationStatus.AlreadyExited)
            {
                allSuccessful = false;
            }
        }

        return new TreeTerminationResult(rootPid, results, allSuccessful);
    }
}
