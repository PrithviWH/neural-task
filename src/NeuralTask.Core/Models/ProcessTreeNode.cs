using System.Collections.Generic;

namespace NeuralTask.Core.Models;

/// <summary>
/// A node in the hierarchical process tree representing a process and its direct child processes.
/// </summary>
public class ProcessTreeNode
{
    public ProcessSnapshot Process { get; }
    public ProcessTreeNode? Parent { get; internal set; }
    public List<ProcessTreeNode> Children { get; } = new();
    public bool IsOrphaned { get; internal set; }

    public ProcessTreeNode(ProcessSnapshot process)
    {
        Process = process;
    }
}
