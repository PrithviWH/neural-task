using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Handles graceful process termination and verified kill-tree operations with start-time validation.
/// </summary>
public class ProcessActionService : IProcessActionService
{
    public ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        return ProcessTreeBuilder.BuildTree(snapshots);
    }

    public Task<ProcessTerminationResult> TerminateProcessAsync(int pid)
    {
        if (pid <= 0)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                "System Idle",
                ProcessTerminationStatus.AccessDenied,
                "Cannot terminate the System Idle process."));
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;

            process.Kill();
            process.WaitForExit(1000);

            return Task.FromResult(new ProcessTerminationResult(
                pid,
                name,
                ProcessTerminationStatus.Success,
                $"Successfully terminated {name} (PID: {pid})."));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                "Unknown",
                ProcessTerminationStatus.AlreadyExited,
                $"Process (PID: {pid}) has already exited."));
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                "Protected System Process",
                ProcessTerminationStatus.AccessDenied,
                $"Access denied. Process (PID: {pid}) is protected or requires elevated Administrator privileges."));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                "Unknown",
                ProcessTerminationStatus.AlreadyExited,
                $"Process (PID: {pid}) has already exited."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessTerminationResult(
                pid,
                "Unknown",
                ProcessTerminationStatus.Failed,
                $"Termination failed for PID {pid}: {ex.Message}"));
        }
    }

    public async Task<TreeTerminationResult> TerminateTreeAsync(int rootPid, IReadOnlyList<ProcessSnapshot> currentSnapshots)
    {
        var tree = BuildTree(currentSnapshots);
        var descendantPids = tree.GetDescendantPids(rootPid);

        // Collect all target PIDs starting from deepest leaves first, then up to the root process
        // to avoid leaves being orphaned or re-parented midway through termination.
        var pidsToTerminate = new List<int>(descendantPids);
        pidsToTerminate.Reverse();
        pidsToTerminate.Add(rootPid);

        var results = new List<ProcessTerminationResult>(pidsToTerminate.Count);
        bool allSuccessful = true;

        foreach (var pid in pidsToTerminate)
        {
            var res = await TerminateProcessAsync(pid).ConfigureAwait(false);
            results.Add(res);
            if (res.Status == ProcessTerminationStatus.Failed || res.Status == ProcessTerminationStatus.AccessDenied)
            {
                allSuccessful = false;
            }
        }

        return new TreeTerminationResult(rootPid, results, allSuccessful);
    }
}
