using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Implements versioned JSON and spreadsheet-compatible CSV snapshot exports (PRD §6.3 / §8.2).
/// Strictly local-only file writes, zero external telemetry.
/// </summary>
public class ExportService : IExportService
{
    public const string CurrentSchemaVersion = "1.0.0";
    public const string ProductName = "NeuralTask";

    public string ExportToJson(
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        if (processes == null) throw new ArgumentNullException(nameof(processes));

        var processEntries = processes.Select(p =>
        {
            var flags = new List<string>();
            if (heuristicResults != null && heuristicResults.TryGetValue(p.Pid, out var result) && result.HasFlags)
            {
                flags.AddRange(result.Flags.Select(f => f.Reason));
            }

            return new
            {
                pid = p.Pid,
                name = p.Name,
                parentPid = p.ParentPid,
                cpuPercent = Math.Round(p.CpuPercent, 2),
                ramBytes = p.RamBytes,
                ramMb = Math.Round(p.RamBytes / (1024.0 * 1024.0), 2),
                diskBytesPerSec = Math.Round(p.DiskBytesPerSec, 2),
                networkBytesPerSec = Math.Round(p.NetworkBytesPerSec, 2),
                executablePath = p.ExecutablePath,
                isAccessDenied = p.IsAccessDenied,
                heuristicFlags = flags
            };
        }).ToList();

        var document = new
        {
            schemaVersion = CurrentSchemaVersion,
            product = ProductName,
            generatedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
            systemMetrics = new
            {
                cpuPercent = Math.Round(system.CpuPercent, 2),
                usedMemoryBytes = system.UsedMemoryBytes,
                totalMemoryBytes = system.TotalMemoryBytes,
                availableMemoryBytes = system.AvailableMemoryBytes,
                memoryPercent = Math.Round(system.MemoryPercent, 2),
                processIoBytesPerSec = Math.Round(system.ProcessIoBytesPerSec, 2),
                networkReceivedBytesPerSec = Math.Round(system.NetworkReceivedBytesPerSec, 2),
                networkSentBytesPerSec = Math.Round(system.NetworkSentBytesPerSec, 2),
                processorName = system.ProcessorName,
                logicalCoreCount = system.LogicalCoreCount,
                osVersion = system.OsVersion,
                processCount = system.ProcessCount,
                uptime = system.Uptime.ToString()
            },
            processCount = processEntries.Count,
            processes = processEntries
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(document, options);
    }

    public string ExportToCsv(
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        if (processes == null) throw new ArgumentNullException(nameof(processes));

        var sb = new StringBuilder();

        // 1. RFC 4180 Standard Header
        sb.AppendLine("PID,Name,ParentPID,CPU_Percent,RAM_Bytes,RAM_MB,Disk_Bytes_Per_Sec,Network_Bytes_Per_Sec,Executable_Path,Is_Access_Denied,Heuristic_Flags");

        // 2. Process Rows
        foreach (var p in processes)
        {
            var flagsString = string.Empty;
            if (heuristicResults != null && heuristicResults.TryGetValue(p.Pid, out var result) && result.HasFlags)
            {
                flagsString = string.Join("; ", result.Flags.Select(f => f.Reason));
            }

            double ramMb = p.RamBytes / (1024.0 * 1024.0);

            sb.Append(p.Pid.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsvField(p.Name)).Append(',');
            sb.Append(p.ParentPid.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.CpuPercent.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.RamBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(ramMb.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.DiskBytesPerSec.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.NetworkBytesPerSec.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsvField(p.ExecutablePath ?? string.Empty)).Append(',');
            sb.Append(p.IsAccessDenied ? "True" : "False").Append(',');
            sb.Append(EscapeCsvField(flagsString));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task ExportToFileAsync(
        string filePath,
        ExportFormat format,
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or whitespace.", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string content = format switch
        {
            ExportFormat.Json => ExportToJson(system, processes, heuristicResults),
            ExportFormat.Csv => ExportToCsv(system, processes, heuristicResults),
            _ => throw new ArgumentOutOfRangeException(nameof(format), $"Unsupported format: {format}")
        };

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";

        bool mustQuote = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (mustQuote)
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return $"\"{field}\"";
    }
}
