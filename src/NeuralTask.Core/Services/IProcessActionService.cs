using System.Collections.Generic;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public enum ProcessTerminationStatus
{
    Success,
    AccessDenied,
    AlreadyExited,
    Failed
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
    Task<ProcessTerminationResult> TerminateProcessAsync(int pid);
    Task<TreeTerminationResult> TerminateTreeAsync(int rootPid, IReadOnlyList<ProcessSnapshot> currentSnapshots);
    ProcessTree BuildTree(IReadOnlyList<ProcessSnapshot> snapshots);
}
