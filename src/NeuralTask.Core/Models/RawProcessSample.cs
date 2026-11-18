using System;

namespace NeuralTask.Core.Models;

/// <summary>
/// Low-level raw metrics captured directly from the OS or a mock source before delta calculation.
/// </summary>
public record RawProcessSample(
    int Pid,
    string Name,
    int ParentPid,
    long KernelTimeTicks,
    long UserTimeTicks,
    long WorkingSetBytes,
    ulong ReadTransferBytes,
    ulong WriteTransferBytes,
    ulong OtherTransferBytes,
    DateTimeOffset? StartTime,
    string? ExecutablePath,
    bool IsAccessDenied
);
