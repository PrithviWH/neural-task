using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// User-configurable sensitivity for the local heuristic engine.
/// Allows tech/developer users to tune out false-positives for unsigned local dev tools.
/// </summary>
public enum HeuristicSensitivity
{
    Conservative, // Default: Suppresses alerts on normal dev tools (Programs/npm/scoop/cargo)
    Standard,     // Standard anomaly detection
    Aggressive    // Flags all unsigned binaries and all AppData paths
}

/// <summary>
/// Local-only heuristic triage engine (PRD §8.4).
/// Transparent, explainable rules flagging processes worth a second look.
/// EXPLICIT DISCLAIMER: This is NOT an antivirus or malware scanner.
/// </summary>
public interface IHeuristicFlagEngine
{
    HeuristicSensitivity Sensitivity { get; set; }

    /// <summary>
    /// Evaluates the complete set of process snapshots and returns heuristic flag results per PID.
    /// Calculates statistical baselines across the entire snapshot population (e.g. handle counts).
    /// </summary>
    IReadOnlyDictionary<int, ProcessHeuristicResult> Evaluate(IReadOnlyList<ProcessSnapshot> snapshots);

    /// <summary>
    /// Evaluates a single process snapshot against the heuristic rules with a specified handle count threshold.
    /// </summary>
    ProcessHeuristicResult EvaluateSingle(ProcessSnapshot snapshot, int handleCountPercentileThreshold = 3500);
}
