using System.Collections.Generic;

namespace NeuralTask.Core.Models;

/// <summary>
/// Root structure containing the forest of process trees and lookup indexes.
/// </summary>
public class ProcessTree
{
    public IReadOnlyList<ProcessTreeNode> RootNodes { get; }
    public IReadOnlyDictionary<int, ProcessTreeNode> AllNodesByPid { get; }

    public ProcessTree(
        IReadOnlyList<ProcessTreeNode> rootNodes,
        IReadOnlyDictionary<int, ProcessTreeNode> allNodesByPid)
    {
        RootNodes = rootNodes;
        AllNodesByPid = allNodesByPid;
    }

    /// <summary>
    /// Traverses and collects all descendant PIDs under the given target PID.
    /// Used for kill-tree operations to terminate process families safely.
    /// </summary>
    public IReadOnlyList<int> GetDescendantPids(int pid)
    {
        var descendants = new List<int>();
        if (!AllNodesByPid.TryGetValue(pid, out var rootNode))
        {
            return descendants;
        }

        var queue = new Queue<ProcessTreeNode>(rootNode.Children);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            descendants.Add(current.Process.Pid);
            foreach (var child in current.Children)
            {
                queue.Enqueue(child);
            }
        }

        return descendants;
    }
}
