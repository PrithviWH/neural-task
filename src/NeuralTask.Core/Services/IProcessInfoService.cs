using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service for generating developer-friendly, structured process telemetry reports (PRD §6.2 - Feature 2.3).
/// Includes asynchronous non-blocking SHA-256 binary hashing, command-line queries, and signer details.
/// </summary>
public interface IProcessInfoService
{
    /// <summary>
    /// Generates a structured ProcessReport asynchronously without blocking the caller.
    /// </summary>
    Task<ProcessReport> GenerateReportAsync(ProcessSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the SHA-256 hash of an executable asynchronously with in-memory caching.
    /// </summary>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the full command line of the process via native Win32/WMI queries.
    /// </summary>
    string? GetCommandLine(int pid);

    /// <summary>
    /// Extracts human-readable Authenticode signer information (Subject / Issuer / Catalog).
    /// </summary>
    string GetSignerInfo(string? executablePath);
}
