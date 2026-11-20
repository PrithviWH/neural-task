using System;

namespace NeuralTask.Core.Models;

/// <summary>
/// Immutable snapshot of an operating system process at a point in time.
/// </summary>
public record ProcessSnapshot(
    int Pid,
    string Name,
    int ParentPid,
    double CpuPercent,
    long RamBytes,
    double DiskBytesPerSec,
    double? NetworkBytesPerSec = null,
    DateTimeOffset? StartTime = null,
    string? ExecutablePath = null,
    bool IsAccessDenied = false,
    int HandleCount = 0
);
