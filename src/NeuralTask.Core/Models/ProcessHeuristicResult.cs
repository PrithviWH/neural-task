using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralTask.Core.Models;

/// <summary>
/// Aggregated heuristic evaluation result for a running process.
/// Every flagged process carries full explainable reasons with no black-box booleans.
/// </summary>
public record ProcessHeuristicResult(
    int Pid,
    string ProcessName,
    IReadOnlyList<ProcessHeuristicFlag> Flags
)
{
    public static readonly ProcessHeuristicResult Clean = new(0, string.Empty, Array.Empty<ProcessHeuristicFlag>());

    public bool HasFlags => Flags.Count > 0;

    public int FlagCount => Flags.Count;

    public HeuristicSeverity MaxSeverity =>
        Flags.Count > 0 ? Flags.Max(f => f.Severity) : HeuristicSeverity.Info;

    public string CombinedSummary =>
        Flags.Count > 0 ? string.Join("; ", Flags.Select(f => f.Title)) : "No anomalies detected";

    public string TooltipText =>
        Flags.Count > 0
            ? string.Join("\n", Flags.Select(f => $"• {f.Title}: {f.Reason}"))
            : "No heuristic anomalies detected.";
}
