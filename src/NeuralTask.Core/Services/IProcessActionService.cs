using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public enum ProcessTerminationStatus
{
    Success,
    AccessDenied,
    AlreadyExited,
    Failed,
    Cancelled,
    IdentityMismatch,
    RefusedAmbiguousIdentity
}

public record ProcessTerminationResult(
    int Pid,
    string ProcessName,
    ProcessTerminationStatus Status,
    string Message
);

public record TreeTerminationResult(
    int RootPid,
    IReadOnlyList<ProcessTerminationResult> ProcessResults,
    bool OverallSuccess
);

public interface IProcessActionService
{
    Task<ProcessTerminationResult> TerminateProcessAsync(
        int pid,
        string? expectedProcessName = null,
        DateTimeOffset? expectedStartTime = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<TreeTerminationResult> TerminateTreeAsync(
        int rootPid,
        IReadOnlyList<ProcessSnapshot> currentSnapshots,
        CancellationToken cancellationToken = default);

    ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots);
}
