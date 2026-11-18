using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessActionTests
{
    private readonly ProcessActionService _service = new();

    [Fact]
    public async Task TerminateProcessAsync_InvalidPid_ReturnsAlreadyExitedOrFailed()
    {
        // PID 9999999 is nonexistent
        var result = await _service.TerminateProcessAsync(9999999);

        Assert.Equal(ProcessTerminationStatus.AlreadyExited, result.Status);
        Assert.Contains("already exited", result.Message, StringComparison.OrdinalIgnoreCase);
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
}
