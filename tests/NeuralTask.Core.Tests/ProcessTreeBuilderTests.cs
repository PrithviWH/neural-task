using System;
using System.Collections.Generic;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessTreeBuilderTests
{
    [Fact]
    public void BuildTree_CorrectlyConstructsParentChildHierarchy()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        var snapshots = new List<ProcessSnapshot>
        {
            new(100, "root.exe", 0, 1.0, 1000, 0, 0, baseTime, @"C:\root.exe", false),
            new(200, "child.exe", 100, 2.0, 2000, 0, 0, baseTime.AddMinutes(5), @"C:\child.exe", false),
            new(300, "grandchild.exe", 200, 0.5, 500, 0, 0, baseTime.AddMinutes(10), @"C:\grandchild.exe", false)
        };

        // Act
        var tree = ProcessTreeBuilder.BuildTree(snapshots);

        // Assert
        Assert.Single(tree.RootNodes);
        var root = tree.RootNodes[0];
        Assert.Equal(100, root.Process.Pid);
        Assert.False(root.IsOrphaned);
        Assert.Single(root.Children);

        var child = root.Children[0];
        Assert.Equal(200, child.Process.Pid);
        Assert.Same(root, child.Parent);
        Assert.False(child.IsOrphaned);
        Assert.Single(child.Children);

        var grandchild = child.Children[0];
        Assert.Equal(300, grandchild.Process.Pid);
        Assert.Same(child, grandchild.Parent);
        Assert.False(grandchild.IsOrphaned);
        Assert.Empty(grandchild.Children);

        // Verify Kill-Tree descendant collection
        var descendantsOfRoot = tree.GetDescendantPids(100);
        Assert.Equal(2, descendantsOfRoot.Count);
        Assert.Contains(200, descendantsOfRoot);
        Assert.Contains(300, descendantsOfRoot);
    }

    [Fact]
    public void BuildTree_IdentifiesOrphanWhenParentHasExited()
    {
        // Arrange: Child points to PID 9999 which is no longer running
        var snapshots = new List<ProcessSnapshot>
        {
            new(500, "orphan.exe", 9999, 0.0, 1000, 0, 0, DateTimeOffset.UtcNow, @"C:\orphan.exe", false)
        };

        // Act
        var tree = ProcessTreeBuilder.BuildTree(snapshots);

        // Assert
        Assert.Single(tree.RootNodes);
        var orphan = tree.RootNodes[0];
        Assert.Equal(500, orphan.Process.Pid);
        Assert.True(orphan.IsOrphaned);
        Assert.Null(orphan.Parent);
    }

    [Fact]
    public void BuildTree_IdentifiesOrphanWhenParentPidIsReusedAfterParentExited()
    {
        // Arrange:
        // Child started at 10:00.
        // PID 800 was recycled and assigned to a new process that started at 10:15 (AFTER child).
        var t1000 = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var t1015 = new DateTimeOffset(2026, 11, 18, 10, 15, 0, TimeSpan.Zero);

        var snapshots = new List<ProcessSnapshot>
        {
            // Newer process that recycled PID 800
            new(800, "new_recycled_proc.exe", 1, 0.0, 2000, 0, 0, t1015, @"C:\new.exe", false),
            // Child whose original parent had PID 800 but died
            new(900, "surviving_child.exe", 800, 0.0, 1500, 0, 0, t1000, @"C:\child.exe", false)
        };

        // Act
        var tree = ProcessTreeBuilder.BuildTree(snapshots);

        // Assert
        // Both processes should be root nodes; surviving_child must NOT be parented under new_recycled_proc
        Assert.Equal(2, tree.RootNodes.Count);

        var recycledNode = tree.AllNodesByPid[800];
        var childNode = tree.AllNodesByPid[900];

        Assert.Empty(recycledNode.Children);
        Assert.Null(childNode.Parent);
        Assert.True(childNode.IsOrphaned, "Child should be flagged as orphaned due to PID recycling");
    }

    [Fact]
    public void BuildTree_SafelyHandlesCircularReferencesWithoutCrashing()
    {
        // Arrange: A points to B and B points to A
        var now = DateTimeOffset.UtcNow;
        var snapshots = new List<ProcessSnapshot>
        {
            new(10, "procA.exe", 20, 0.0, 1000, 0, 0, now, null, false),
            new(20, "procB.exe", 10, 0.0, 1000, 0, 0, now, null, false)
        };

        // Act & Assert (Must not throw or stack overflow)
        var tree = ProcessTreeBuilder.BuildTree(snapshots);

        Assert.NotNull(tree);
        Assert.NotEmpty(tree.RootNodes);
    }
}
