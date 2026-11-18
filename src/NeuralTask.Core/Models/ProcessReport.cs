using System;
using System.Text;

namespace NeuralTask.Core.Models;

/// <summary>
/// Structured telemetry report for a process (PRD §6.2 - Feature 2.3).
/// Formatted cleanly for pasting into chat messages, issue trackers, and bug reports.
/// </summary>
public record ProcessReport(
    int Pid,
    string Name,
    int ParentPid,
    string? ExecutablePath,
    string? CommandLine,
    string Sha256Hash,
    string SignerInfo,
    double CpuPercent,
    long RamBytes,
    double IoBytesPerSec,
    DateTimeOffset? StartTime,
    string FormattedText
)
{
    public static string FormatReport(
        int pid,
        string name,
        int parentPid,
        string? executablePath,
        string? commandLine,
        string sha256Hash,
        string signerInfo,
        double cpuPercent,
        long ramBytes,
        double ioBytesPerSec,
        DateTimeOffset? startTime)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("================================================================================");
        sb.AppendLine("NEURALTASK // PROCESS TELEMETRY REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Process Name : {name}");
        sb.AppendLine($"PID          : {pid}");
        sb.AppendLine($"Parent PID   : {parentPid}");
        sb.AppendLine($"Executable   : {executablePath ?? "[Unavailable // Access Denied]"}");
        sb.AppendLine($"Command Line : {commandLine ?? "[Unavailable // Access Denied]"}");
        sb.AppendLine($"SHA-256 Hash : {sha256Hash}");
        sb.AppendLine($"Signer Info  : {signerInfo}");
        sb.AppendLine($"CPU Usage    : {cpuPercent:0.00}%");
        sb.AppendLine($"Memory (RAM) : {ramBytes / (1024.0 * 1024.0):0.0} MB ({ramBytes:N0} bytes)");
        sb.AppendLine($"Process I/O  : {FormatBytesRate(ioBytesPerSec)}");
        sb.AppendLine($"Start Time   : {(startTime.HasValue ? startTime.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'zzz") : "[Unavailable]")}");
        sb.AppendLine($"Report Date  : {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}");
        sb.AppendLine("================================================================================");
        return sb.ToString();
    }

    private static string FormatBytesRate(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "—";
        if (bytesPerSec < 1024) return $"{bytesPerSec:0.0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
    }
}
