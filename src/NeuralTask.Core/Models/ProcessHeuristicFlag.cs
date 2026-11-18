using System;

namespace NeuralTask.Core.Models;

public enum HeuristicSeverity
{
    Info,      // Low interest / informational
    Notice,    // Notable anomaly (e.g. unsigned non-temp tool)
    Warning    // Significant anomaly (e.g. unsigned in temp, or mimicking system binary)
}

/// <summary>
/// Represents a single transparent, explainable heuristic flag per PRD §8.4.
/// NOT a malware detection signal; carries an explainable reason string for user triage.
/// </summary>
public record ProcessHeuristicFlag(
    string RuleId,
    string Title,
    string Reason,
    HeuristicSeverity Severity
);
