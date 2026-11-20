using System;
using System.IO;
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

    [Fact]
    public void SetEntryEnabled_StartupFolder_DisablesAndReEnablesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"startup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalFile = Path.Combine(tempDir, "sample_tool.lnk");

        try
        {
            File.WriteAllText(originalFile, "mock shortcut content");

            var entry = new StartupEntry(
                id: "test_entry_1",
                name: "sample_tool",
                command: originalFile,
                executablePath: null,
                location: tempDir,
                sourceType: StartupSourceType.StartupFolderUser,
                isEnabled: true,
                filePath: originalFile);

            // 1. Disable the item
            bool disabledResult = _engine.SetEntryEnabled(entry, false);
            Assert.True(disabledResult);
            Assert.False(entry.IsEnabled);

            var expectedDisabledFile = originalFile + ".disabled";
            Assert.Equal(expectedDisabledFile, entry.FilePath);
            Assert.Equal(expectedDisabledFile, entry.Command);
            Assert.False(File.Exists(originalFile));
            Assert.True(File.Exists(expectedDisabledFile));
            // Crucial: directory itself must NOT be renamed or deleted!
            Assert.True(Directory.Exists(tempDir));
            Assert.Equal(tempDir, entry.Location);

            // 2. Re-enable the item
            bool reEnabledResult = _engine.SetEntryEnabled(entry, true);
            Assert.True(reEnabledResult);
            Assert.True(entry.IsEnabled);
            Assert.Equal(originalFile, entry.FilePath);
            Assert.Equal(originalFile, entry.Command);
            Assert.True(File.Exists(originalFile));
            Assert.False(File.Exists(expectedDisabledFile));
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SetEntryEnabled_StartupFolder_HandlesMissingFileSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"startup_missing_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var missingFile = Path.Combine(tempDir, "does_not_exist.lnk");

        try
        {
            var entry = new StartupEntry(
                id: "test_missing",
                name: "missing",
                command: missingFile,
                executablePath: null,
                location: tempDir,
                sourceType: StartupSourceType.StartupFolderUser,
                isEnabled: true,
                filePath: missingFile);

            bool result = _engine.SetEntryEnabled(entry, false);
            Assert.False(result);
            Assert.True(entry.IsEnabled); // State unchanged on failure
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SetEntryEnabled_StartupFolder_HandlesRenameConflictSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"startup_conflict_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalFile = Path.Combine(tempDir, "conflict_app.exe");
        var staleDisabledFile = originalFile + ".disabled";

        try
        {
            File.WriteAllText(originalFile, "current binary");
            File.WriteAllText(staleDisabledFile, "stale conflicting binary");

            var entry = new StartupEntry(
                id: "test_conflict",
                name: "conflict_app",
                command: originalFile,
                executablePath: originalFile,
                location: tempDir,
                sourceType: StartupSourceType.StartupFolderUser,
                isEnabled: true,
                filePath: originalFile);

            // Safety rule: If destination exists, SetEntryEnabled must abort without deleting or overwriting any file
            bool result = _engine.SetEntryEnabled(entry, false);
            Assert.False(result);
            Assert.True(entry.IsEnabled); // State unchanged on abort
            Assert.True(File.Exists(staleDisabledFile));
            Assert.True(File.Exists(originalFile));
            Assert.Equal("stale conflicting binary", File.ReadAllText(staleDisabledFile));
            Assert.Equal("current binary", File.ReadAllText(originalFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SetEntryEnabled_StartupFolder_ReEnableCollisionSafelyAborts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"startup_re_enable_conflict_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalFile = Path.Combine(tempDir, "app.lnk");
        var disabledFile = originalFile + ".disabled";

        try
        {
            File.WriteAllText(disabledFile, "disabled shortcut");
            File.WriteAllText(originalFile, "newly recreated shortcut");

            var entry = new StartupEntry(
                id: "test_re_enable_conflict",
                name: "app",
                command: disabledFile,
                executablePath: null,
                location: tempDir,
                sourceType: StartupSourceType.StartupFolderUser,
                isEnabled: false,
                filePath: disabledFile);

            // When re-enabling, if original destination already exists, must abort without deleting it
            bool result = _engine.SetEntryEnabled(entry, true);
            Assert.False(result);
            Assert.False(entry.IsEnabled);
            Assert.True(File.Exists(disabledFile));
            Assert.True(File.Exists(originalFile));
            Assert.Equal("disabled shortcut", File.ReadAllText(disabledFile));
            Assert.Equal("newly recreated shortcut", File.ReadAllText(originalFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void EnumerateStartupFolder_DistinctExtensionsDoNotCollide()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"startup_enum_collision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var exeFile = Path.Combine(tempDir, "Discord.exe");
        var lnkFile = Path.Combine(tempDir, "Discord.lnk");

        try
        {
            File.WriteAllText(exeFile, "mock exe");
            File.WriteAllText(lnkFile, "mock lnk");

            var destination = new System.Collections.Generic.Dictionary<string, StartupEntry>();
            var method = typeof(StartupScoreEngine).GetMethod("EnumerateStartupFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);

            method.Invoke(_engine, new object[] { tempDir, StartupSourceType.StartupFolderUser, destination });

            // Both items should be present, sharing readable Name "Discord", but having distinct IDs
            Assert.Equal(2, destination.Count);
            Assert.True(destination.ContainsKey("StartupFolderUser_Discord.exe"));
            Assert.True(destination.ContainsKey("StartupFolderUser_Discord.lnk"));

            var exeEntry = destination["StartupFolderUser_Discord.exe"];
            var lnkEntry = destination["StartupFolderUser_Discord.lnk"];

            Assert.Equal("Discord", exeEntry.Name);
            Assert.Equal("Discord", lnkEntry.Name);
            Assert.Equal(exeFile, exeEntry.FilePath);
            Assert.Equal(lnkFile, lnkEntry.FilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void StartupEntry_ConstructsWithExplicitFilePathAndFallbacks()
    {
        var folderEntry = new StartupEntry(
            id: "folder_1",
            name: "tool",
            command: @"C:\Folder\tool.lnk",
            executablePath: null,
            location: @"C:\Folder",
            sourceType: StartupSourceType.StartupFolderUser,
            isEnabled: true);

        // Auto-falls back to command if filePath was not explicitly provided for folder source
        Assert.Equal(@"C:\Folder\tool.lnk", folderEntry.FilePath);

        var registryEntry = new StartupEntry(
            id: "reg_1",
            name: "regtool",
            command: @"C:\Tool\regtool.exe",
            executablePath: @"C:\Tool\regtool.exe",
            location: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            sourceType: StartupSourceType.RegistryRunCurrentUser,
            isEnabled: true);

        // Registry entry does not have a startup folder FilePath
        Assert.Null(registryEntry.FilePath);
    }
}
