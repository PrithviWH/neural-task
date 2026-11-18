using System;

namespace NeuralTask.Core.Models;

/// <summary>
/// Fully explainable breakdown of a startup item's impact score.
/// Decomposes the 0-100 score into time-contribution and resource-contribution per PRD §8.3.
/// </summary>
public record StartupScoreBreakdown(
    int TotalScore,
    double TimeScore,
    double ResourceScore,
    double CpuTimeSeconds,
    long IoBytes,
    double ActiveDurationSeconds,
    double BootDelaySeconds,
    string ImpactRating,
    string Summary
)
{
    public double TimeContributionPercent =>
        TotalScore > 0 ? Math.Round((TimeScore / TotalScore) * 100.0, 1) : 0.0;

    public double ResourceContributionPercent =>
        TotalScore > 0 ? Math.Round((ResourceScore / TotalScore) * 100.0, 1) : 0.0;
}
