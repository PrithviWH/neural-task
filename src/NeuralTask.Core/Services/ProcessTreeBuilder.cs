using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Constructs a hierarchical process tree from a flat list of process snapshots.
/// Robustly handles orphaned processes, PID recycling, and circular parent references.
/// </summary>
public static class ProcessTreeBuilder
{
    public static ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var allNodesByPid = new Dictionary<int, ProcessTreeNode>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            allNodesByPid[snapshot.Pid] = new ProcessTreeNode(snapshot);
        }

        var rootNodes = new List<ProcessTreeNode>();

        foreach (var node in allNodesByPid.Values)
        {
            var parentPid = node.Process.ParentPid;

            // Root cases: No valid parent PID or self-parenting
            if (parentPid <= 0 || parentPid == node.Process.Pid)
            {
                rootNodes.Add(node);
                continue;
            }

            // Case 1: Parent PID is not among running processes (original parent exited)
            if (!allNodesByPid.TryGetValue(parentPid, out var potentialParent))
            {
                node.IsOrphaned = true;
                rootNodes.Add(node);
                continue;
            }

            // Case 2: PID Recycling detection via StartTime comparison
            // If the putative parent started AFTER the child process was spawned,
            // the original parent must have terminated and Windows recycled the PID.
            if (node.Process.StartTime.HasValue &&
                potentialParent.Process.StartTime.HasValue &&
                potentialParent.Process.StartTime.Value > node.Process.StartTime.Value)
            {
                node.IsOrphaned = true;
                rootNodes.Add(node);
                continue;
            }

            // Case 3: Circular reference prevention
            if (WouldCauseCycle(potentialParent, node.Process.Pid))
            {
                node.IsOrphaned = true;
                rootNodes.Add(node);
                continue;
            }

            // Valid parent-child relationship
            node.Parent = potentialParent;
            potentialParent.Children.Add(node);
        }

        return new ProcessTree(rootNodes, allNodesByPid);
    }

    private static bool WouldCauseCycle(ProcessTreeNode potentialParent, int targetPid)
    {
        var current = potentialParent;
        while (current != null)
        {
            if (current.Process.Pid == targetPid)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }
}
