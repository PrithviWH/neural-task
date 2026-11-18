using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service providing versioned JSON and flat spreadsheet-friendly CSV exports of live or historical process snapshots.
/// Local-only, zero network footprint (PRD §6.3 / §7).
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Produces a versioned JSON representation of the current system and process telemetry.
    /// Includes schemaVersion for backwards and forwards compatibility tooling.
    /// </summary>
    string ExportToJson(
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null);

    /// <summary>
    /// Produces an RFC 4180 compliant CSV string of running processes and metrics,
    /// directly importable into Microsoft Excel, Google Sheets, or LibreOffice Calc.
    /// </summary>
    string ExportToCsv(
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null);

    /// <summary>
    /// Writes the snapshot to a local file in the specified format.
    /// </summary>
    Task ExportToFileAsync(
        string filePath,
        ExportFormat format,
        SystemMetricsSnapshot system,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null,
        CancellationToken cancellationToken = default);
}
