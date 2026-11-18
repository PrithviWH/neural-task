using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class OrphanIntegrationTests
{
    [Fact]
    public void RealWorld_OrphanedProcess_IsDetectedWhenParentIsTerminated()
    {
        // 1. Launch a parent cmd.exe process which spawns a child powershell process
        var parentStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k powershell -NoProfile -Command Start-Sleep -Seconds 60",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var parent = Process.Start(parentStartInfo);
        Assert.NotNull(parent);
        var parentPid = parent.Id;

        // Allow child time to spawn
        Thread.Sleep(1500);

        // Sample processes to find the child
        using var monitor = new ProcessMonitorService();
        var initialSnapshots = monitor.SampleOnce();

        var childSnapshot = initialSnapshots.FirstOrDefault(s => s.ParentPid == parentPid);
        Assert.NotNull(childSnapshot);
        var childPid = childSnapshot.Pid;

        try
        {
            // 2. Kill the parent process abruptly
            parent.Kill();
            parent.WaitForExit(3000);

            // Small delay to ensure Windows process table reflects parent termination
            Thread.Sleep(500);

            // 3. Collect new snapshot and build tree
            var updatedSnapshots = monitor.SampleOnce();
            var tree = ProcessTreeBuilder.BuildTree(updatedSnapshots);

            // 4. Verify child is still running and flagged as orphaned
            Assert.True(tree.AllNodesByPid.ContainsKey(childPid), "Child process should still be alive");
            var childNode = tree.AllNodesByPid[childPid];

            Assert.True(childNode.IsOrphaned, "Child process must be flagged as IsOrphaned when parent is terminated");
            Assert.Null(childNode.Parent);
            Assert.Contains(childNode, tree.RootNodes);
        }
        finally
        {
            // 5. Clean up child process
            try
            {
                using var child = Process.GetProcessById(childPid);
                child.Kill();
            }
            catch
            {
                // Child already terminated
            }
        }
    }
}
