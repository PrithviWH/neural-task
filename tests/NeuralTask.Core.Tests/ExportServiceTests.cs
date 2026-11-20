using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ExportServiceTests
{
    private static SystemMetricsSnapshot CreateDummySystem()
    {
        return new SystemMetricsSnapshot(
            CpuPercent: 18.5,
            TotalMemoryBytes: 16_000_000_000,
            UsedMemoryBytes: 9_600_000_000,
            AvailableMemoryBytes: 6_400_000_000,
            MemoryPercent: 60.0,
            ProcessIoBytesPerSec: 2_500_000,
            NetworkReceivedBytesPerSec: 15_000,
            NetworkSentBytesPerSec: 5_000,
            ProcessorName: "AMD Ryzen 9 7950X",
            LogicalCoreCount: 32,
            OsVersion: "Microsoft Windows 11 Pro",
            ProcessCount: 145,
            Uptime: TimeSpan.FromHours(12)
        );
    }

    private static IReadOnlyList<ProcessSnapshot> CreateDummyProcesses()
    {
        return new List<ProcessSnapshot>
        {
            new ProcessSnapshot(1001, "explorer.exe", 800, 1.2, 120_000_000, 5000, 1000, DateTime.Now.AddHours(-12), @"C:\Windows\explorer.exe", false),
            new ProcessSnapshot(2002, "chrome.exe", 1001, 8.5, 450_000_000, 50000, 10000, DateTime.Now.AddHours(-2), @"C:\Program Files\Google\Chrome\chrome.exe", false),
            new ProcessSnapshot(3003, "suspicious.exe", 1001, 15.0, 50_000_000, 10000, 2000, DateTime.Now.AddMinutes(-5), @"C:\Users\User\Downloads\suspicious.exe", false)
        };
    }

    private static IReadOnlyDictionary<int, ProcessHeuristicResult> CreateDummyHeuristics()
    {
        var flag = new ProcessHeuristicFlag(
            "UNTRUSTED_PATH",
            "Untrusted Execution Path",
            "Executing from Downloads folder: C:\\Users\\User\\Downloads\\suspicious.exe",
            HeuristicSeverity.Warning
        );

        return new Dictionary<int, ProcessHeuristicResult>
        {
            [3003] = new ProcessHeuristicResult(3003, "suspicious.exe", new[] { flag })
        };
    }

    [Fact]
    public void ExportToJson_ContainsSchemaVersionAndValidStructure()
    {
        var service = new ExportService();
        var json = service.ExportToJson(CreateDummySystem(), CreateDummyProcesses(), CreateDummyHeuristics());

        Assert.NotNull(json);
        Assert.NotEmpty(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("NeuralTask", root.GetProperty("product").GetString());
        Assert.True(root.TryGetProperty("generatedAt", out _));

        var system = root.GetProperty("systemMetrics");
        Assert.Equal(18.5, system.GetProperty("cpuPercent").GetDouble());
        Assert.Equal(60.0, system.GetProperty("memoryPercent").GetDouble());
        Assert.Equal("AMD Ryzen 9 7950X", system.GetProperty("processorName").GetString());

        var processes = root.GetProperty("processes");
        Assert.Equal(3, processes.GetArrayLength());

        // Check process with heuristic flags
        var suspicious = processes[2];
        Assert.Equal(3003, suspicious.GetProperty("pid").GetInt32());
        var flags = suspicious.GetProperty("heuristicFlags");
        Assert.Equal(1, flags.GetArrayLength());
        Assert.Contains("Downloads", flags[0].GetString());
    }

    [Fact]
    public void ExportToCsv_ContainsRFC4180HeadersAndProperQuoting()
    {
        var service = new ExportService();
        var csv = service.ExportToCsv(CreateDummySystem(), CreateDummyProcesses(), CreateDummyHeuristics());

        Assert.NotNull(csv);
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Header line
        Assert.StartsWith("PID,Name,ParentPID,CPU_Percent,RAM_Bytes,RAM_MB,Disk_Bytes_Per_Sec,Network_Bytes_Per_Sec,Executable_Path,Is_Access_Denied,Heuristic_Flags", lines[0]);

        // Process lines
        Assert.Equal(4, lines.Length); // 1 header + 3 processes

        // Check suspicious line
        var suspiciousLine = lines[3];
        Assert.StartsWith("3003,\"suspicious.exe\",1001,15.00,50000000", suspiciousLine);
        Assert.Contains("Downloads folder", suspiciousLine);
    }

    [Fact]
    public async Task ExportToFileAsync_WritesJsonAndCsvFilesCorrectly()
    {
        var service = new ExportService();
        var tempJson = Path.Combine(Path.GetTempPath(), $"neuraltask_export_test_{Guid.NewGuid():N}.json");
        var tempCsv = Path.Combine(Path.GetTempPath(), $"neuraltask_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            await service.ExportToFileAsync(tempJson, ExportFormat.Json, CreateDummySystem(), CreateDummyProcesses());
            Assert.True(File.Exists(tempJson));
            var jsonContent = await File.ReadAllTextAsync(tempJson);
            Assert.Contains("\"schemaVersion\": \"1.0.0\"", jsonContent);

            await service.ExportToFileAsync(tempCsv, ExportFormat.Csv, CreateDummySystem(), CreateDummyProcesses());
            Assert.True(File.Exists(tempCsv));
            var csvContent = await File.ReadAllTextAsync(tempCsv);
            Assert.Contains("PID,Name,ParentPID", csvContent);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Export_LiveSystemSnapshot_ProducesValidJsonAndCsv()
    {
        using var monitor = new ProcessMonitorService();
        var metricsService = new SystemMetricsService();
        var heuristicEngine = new HeuristicFlagEngine();
        var exportService = new ExportService();

        var snapshots = monitor.SampleOnce();
        Assert.NotEmpty(snapshots);

        var systemMetrics = metricsService.CollectSnapshot(snapshots);
        Assert.NotNull(systemMetrics);

        var heuristics = heuristicEngine.Evaluate(snapshots);
        Assert.NotNull(heuristics);

        // 1. JSON
        var json = exportService.ExportToJson(systemMetrics, snapshots, heuristics);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(snapshots.Count, root.GetProperty("processCount").GetInt32());

        // 2. CSV
        var csv = exportService.ExportToCsv(systemMetrics, snapshots, heuristics);
        Assert.NotNull(csv);

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        // Header + exactly snapshots.Count lines
        Assert.Equal(snapshots.Count + 1, lines.Length);
        Assert.StartsWith("PID,Name,ParentPID", lines[0]);
    }

    [Fact]
    public void Export_WhenNetworkBytesPerSecIsNull_RendersAsNullInJsonAndEmptyInCsv()
    {
        var service = new ExportService();
        var processes = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(101, "proc.exe", 1, 2.5, 100_000, 500, null, DateTime.Now, @"C:\proc.exe", false)
        };

        var json = service.ExportToJson(CreateDummySystem(), processes);
        using var doc = JsonDocument.Parse(json);
        var procJson = doc.RootElement.GetProperty("processes")[0];
        Assert.Equal(JsonValueKind.Null, procJson.GetProperty("networkBytesPerSec").ValueKind);

        var csv = service.ExportToCsv(CreateDummySystem(), processes);
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        // Format: ...,Disk_Bytes_Per_Sec,Network_Bytes_Per_Sec,Executable_Path,...
        // When Network is null, an empty string is rendered between the commas: "500.00,,\"C:\proc.exe\""
        Assert.Contains("500.00,,\"C:\\proc.exe\"", lines[1]);
    }
}
