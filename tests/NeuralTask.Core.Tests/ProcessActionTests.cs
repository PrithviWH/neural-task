using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessActionTests
{
    private readonly ProcessActionService _service = new();

    [Fact]
    public async Task TerminateProcessAsync_InvalidPid_WithIdentity_ReturnsAlreadyExitedOrFailed()
    {
        // PID 9999999 is nonexistent, but provided with explicit expected identity
        var result = await _service.TerminateProcessAsync(
            9999999,
            expectedProcessName: "nonexistent.exe",
            expectedStartTime: DateTimeOffset.UtcNow);

        Assert.Equal(ProcessTerminationStatus.AlreadyExited, result.Status);
        Assert.Contains("already exited", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_MissingExpectedProcessName_ReturnsRefusedAmbiguousIdentity()
    {
        var currentProc = Process.GetCurrentProcess();
        var result = await _service.TerminateProcessAsync(
            currentProc.Id,
            expectedProcessName: null,
            expectedStartTime: DateTimeOffset.UtcNow);

        Assert.Equal(ProcessTerminationStatus.RefusedAmbiguousIdentity, result.Status);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_MissingExpectedStartTime_ReturnsRefusedAmbiguousIdentity()
    {
        var currentProc = Process.GetCurrentProcess();
        var result = await _service.TerminateProcessAsync(
            currentProc.Id,
            expectedProcessName: currentProc.ProcessName,
            expectedStartTime: null);

        Assert.Equal(ProcessTerminationStatus.RefusedAmbiguousIdentity, result.Status);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_NoIdentityParameters_ReturnsRefusedAmbiguousIdentity()
    {
        var result = await _service.TerminateProcessAsync(1234);

        Assert.Equal(ProcessTerminationStatus.RefusedAmbiguousIdentity, result.Status);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_SystemIdle_ReturnsAccessDenied()
    {
        // PID 0 is System Idle Process
        var result = await _service.TerminateProcessAsync(0);

        Assert.Equal(ProcessTerminationStatus.AccessDenied, result.Status);
    }

    [Fact]
    public async Task TerminateTreeAsync_ValidTree_TerminatesLeavesBeforeParent()
    {
        // Construct a synthetic process tree:
        // Parent: PID 1000, StartTime T0
        // Child 1: PID 1001, ParentPid 1000, StartTime T1 > T0
        // Child 2: PID 1002, ParentPid 1000, StartTime T2 > T0
        // Recycled/Orphan: PID 1003, ParentPid 1000, StartTime T_old < T0 (recycled PID)
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);
        var tOld = t0.AddMinutes(-5); // Started BEFORE parent -> recycled, must NOT be in kill-tree!

        var snapshots = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(1000, "parent.exe", 0, 1.0, 100, 0, 0, t0, null, false),
            new ProcessSnapshot(1001, "child1.exe", 1000, 1.0, 100, 0, 0, t1, null, false),
            new ProcessSnapshot(1002, "child2.exe", 1000, 1.0, 100, 0, 0, t2, null, false),
            new ProcessSnapshot(1003, "recycled_unrelated.exe", 1000, 1.0, 100, 0, 0, tOld, null, false)
        };

        var tree = _service.BuildTree(snapshots);
        var descendants = tree.GetDescendantPids(1000);

        // Verified: only true children (1001, 1002) are identified as descendants; recycled 1003 is excluded!
        Assert.Contains(1001, descendants);
        Assert.Contains(1002, descendants);
        Assert.DoesNotContain(1003, descendants);

        // Attempting termination on synthetic PIDs (which are nonexistent) handles exit gracefully
        var result = await _service.TerminateTreeAsync(1000, snapshots);

        Assert.NotNull(result);
        Assert.Equal(1000, result.RootPid);
        Assert.Equal(3, result.ProcessResults.Count); // 1001, 1002, 1000
    }

    [Fact]
    public async Task RealProcess_SpawnAndKillTree_TerminatesCompletely()
    {
        // Spawn a parent cmd.exe that spawns a child timeout.exe
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c (start /b timeout 30 >nul) & timeout 30 >nul",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var parentProc = Process.Start(psi);
        Assert.NotNull(parentProc);

        try
        {
            await Task.Delay(500); // Allow processes to spawn

            var monitor = new ProcessMonitorService();
            var snapshots = monitor.SampleOnce();

            var tree = _service.BuildTree(snapshots);
            var descendants = tree.GetDescendantPids(parentProc.Id);

            // Terminate the process tree
            var treeResult = await _service.TerminateTreeAsync(parentProc.Id, snapshots);

            Assert.NotNull(treeResult);

            // Wait a brief moment and verify parent process is terminated
            await Task.Delay(300);
            Assert.True(parentProc.HasExited);
        }
        finally
        {
            if (!parentProc.HasExited)
            {
                try { parentProc.Kill(true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TerminateProcessAsync_CancelledToken_ReturnsCancelled()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var result = await _service.TerminateProcessAsync(1234, cancellationToken: cts.Token);

        Assert.Equal(ProcessTerminationStatus.Cancelled, result.Status);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateTreeAsync_CancelledToken_AbortsEarly()
    {
        var snapshots = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(2000, "root.exe", 0, 1.0, 100, 0, 0, DateTime.Now, null, false),
            new ProcessSnapshot(2001, "child.exe", 2000, 1.0, 100, 0, 0, DateTime.Now.AddSeconds(1), null, false)
        };

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var result = await _service.TerminateTreeAsync(2000, snapshots, cancellationToken: cts.Token);

        Assert.False(result.OverallSuccess);
        Assert.NotEmpty(result.ProcessResults);
        Assert.Equal(ProcessTerminationStatus.Cancelled, result.ProcessResults[0].Status);
    }

    [Fact]
    public async Task TerminateProcessAsync_ExecutesAsynchronouslyOffCallerThread()
    {
        var task = Task.Run(async () =>
        {
            var res = await _service.TerminateProcessAsync(
                9999999,
                expectedProcessName: "nonexistent.exe",
                expectedStartTime: DateTimeOffset.UtcNow);
            return res;
        });

        var result = await task;
        Assert.Equal(ProcessTerminationStatus.AlreadyExited, result.Status);
    }

    [Fact]
    public async Task TerminateProcessAsync_SameNameDifferentStartTime_DetectsPidRecyclingAndAborts()
    {
        // Target current process with its exact name, but with an old start time -> simulated PID recycling
        var currentProc = Process.GetCurrentProcess();
        var mismatchedStartTime = DateTimeOffset.UtcNow.AddHours(-2);

        var result = await _service.TerminateProcessAsync(
            currentProc.Id,
            expectedProcessName: currentProc.ProcessName,
            expectedStartTime: mismatchedStartTime);

        Assert.Equal(ProcessTerminationStatus.IdentityMismatch, result.Status);
        Assert.Contains("PID recycling detected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aborted for safety", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_MismatchedProcessName_DetectsIdentityMismatchAndAborts()
    {
        // Use current process PID but with an entirely mismatched process name
        var currentProc = Process.GetCurrentProcess();

        var result = await _service.TerminateProcessAsync(
            currentProc.Id,
            expectedProcessName: "totally_unrelated_process_xyz.exe",
            expectedStartTime: currentProc.StartTime);

        Assert.Equal(ProcessTerminationStatus.IdentityMismatch, result.Status);
        Assert.Contains("Identity mismatch detected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aborted for safety", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateProcessAsync_InaccessibleStartTime_RefusesAmbiguousIdentity()
    {
        // When StartTime access is denied (Win32Exception AccessDenied), termination must be refused rather than falling back to name-only
        var currentProc = Process.GetCurrentProcess();
        var restrictedService = new ProcessActionService(_ => throw new System.ComponentModel.Win32Exception(5, "Access is denied"));

        var result = await restrictedService.TerminateProcessAsync(
            currentProc.Id,
            expectedProcessName: currentProc.ProcessName,
            expectedStartTime: DateTimeOffset.UtcNow);

        Assert.Equal(ProcessTerminationStatus.RefusedAmbiguousIdentity, result.Status);
        Assert.Contains("unable to verify start time", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminateTreeAsync_DescendantMissingIdentity_RefusesAmbiguousIdentity()
    {
        // Child 2001 in topology has no start time in snapshot -> must be refused for ambiguous identity
        var snapshots = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(2000, "root.exe", 0, 1.0, 100, 0, 0, DateTime.Now, null, false),
            new ProcessSnapshot(2001, "child.exe", 2000, 1.0, 100, 0, 0, null, null, false)
        };

        var result = await _service.TerminateTreeAsync(2000, snapshots);

        Assert.False(result.OverallSuccess);
        var childResult = result.ProcessResults.FirstOrDefault(r => r.Pid == 2001);
        Assert.NotNull(childResult);
        Assert.Equal(ProcessTerminationStatus.RefusedAmbiguousIdentity, childResult.Status);
    }
}
