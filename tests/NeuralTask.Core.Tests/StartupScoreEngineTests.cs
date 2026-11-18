using System;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class StartupScoreEngineTests
{
    private readonly StartupScoreEngine _engine = new() { BootWindowSeconds = 45 };

    [Fact]
    public void HeavyOffender_ProducesHighScoreAndExplainableBreakdown()
    {
        // Heavy startup app: active for 35s during 45s window, 8.5s CPU time, 120 MB disk I/O
        var heavyProcess = new ProcessSnapshot(
            Pid: 1234,
            Name: "heavy_launcher.exe",
            ParentPid: 1000,
            CpuPercent: 22.5,
            RamBytes: 450_000_000,
            DiskBytesPerSec: 4_500_000, // 4.5 MB/s
            NetworkBytesPerSec: 0,
            StartTime: DateTime.Now.AddSeconds(-35),
            ExecutablePath: @"C:\Program Files\Heavy\heavy_launcher.exe",
            IsAccessDenied: false
        );

        var score = _engine.CalculateScore(
            "HeavyLauncher",
            @"C:\Program Files\Heavy\heavy_launcher.exe",
            heavyProcess,
            systemUptime: TimeSpan.FromSeconds(45));

        Assert.NotNull(score);
        Assert.InRange(score.TotalScore, 65, 100);
        Assert.True(score.TimeScore > 25);
        Assert.True(score.ResourceScore > 25);
        Assert.Equal("Severe", score.ImpactRating);
        Assert.Contains("Active for", score.Summary);
        Assert.Contains("CPU time", score.Summary);
    }

    [Fact]
    public void LightweightUtility_ProducesLowScore()
    {
        // Lightweight background utility: minimal CPU, almost 0 I/O
        var lightProcess = new ProcessSnapshot(
            Pid: 5678,
            Name: "tray_helper.exe",
            ParentPid: 1000,
            CpuPercent: 0.1,
            RamBytes: 8_000_000,
            DiskBytesPerSec: 10_000,
            NetworkBytesPerSec: 0,
            StartTime: DateTime.Now.AddSeconds(-5),
            ExecutablePath: @"C:\Program Files\Util\tray_helper.exe",
            IsAccessDenied: false
        );

        var score = _engine.CalculateScore("TrayHelper", @"C:\Program Files\Util\tray_helper.exe", lightProcess);

        Assert.NotNull(score);
        Assert.InRange(score.TotalScore, 0, 35);
        Assert.Contains(score.ImpactRating, new[] { "Minimal", "Low" });
    }

    [Fact]
    public void ScoringMath_IsBoundedBetweenZeroAndOneHundred()
    {
        // Test extreme values
        var extremeProcess = new ProcessSnapshot(
            Pid: 9999,
            Name: "extreme.exe",
            ParentPid: 1000,
            CpuPercent: 100.0,
            RamBytes: 2_000_000_000,
            DiskBytesPerSec: 500_000_000, // 500 MB/s
            NetworkBytesPerSec: 0,
            StartTime: DateTime.Now.AddSeconds(-100),
            ExecutablePath: @"C:\extreme.exe",
            IsAccessDenied: false
        );

        var score = _engine.CalculateScore("ExtremeApp", @"C:\extreme.exe", extremeProcess);

        Assert.True(score.TotalScore <= 100);
        Assert.True(score.TimeScore <= 50);
        Assert.True(score.ResourceScore <= 50);
    }

    [Fact]
    public void BreakdownPercentages_SumToApproximatelyOneHundred()
    {
        var proc = new ProcessSnapshot(
            Pid: 4444,
            Name: "app.exe",
            ParentPid: 1000,
            CpuPercent: 4.5,
            RamBytes: 150_000_000,
            DiskBytesPerSec: 1_000_000,
            NetworkBytesPerSec: 0,
            StartTime: DateTime.Now.AddSeconds(-20),
            ExecutablePath: @"C:\app.exe",
            IsAccessDenied: false
        );

        var score = _engine.CalculateScore("App", @"C:\app.exe", proc);

        if (score.TotalScore > 0)
        {
            var sum = score.TimeContributionPercent + score.ResourceContributionPercent;
            Assert.InRange(sum, 99.0, 101.0);
        }
    }
}
