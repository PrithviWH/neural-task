using System;

namespace NeuralTask.Core.Models;

public enum StartupSourceType
{
    RegistryRunCurrentUser,
    RegistryRunLocalMachine,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTask
}

public class StartupEntry
{
    public string Id { get; }
    public string Name { get; }
    public string Command { get; }
    public string? ExecutablePath { get; }
    public string Location { get; }
    public StartupSourceType SourceType { get; }
    public bool IsEnabled { get; set; }
    public int? AssociatedPid { get; set; }
    public StartupScoreBreakdown? Score { get; set; }

    public StartupEntry(
        string id,
        string name,
        string command,
        string? executablePath,
        string location,
        StartupSourceType sourceType,
        bool isEnabled,
        int? associatedPid = null,
        StartupScoreBreakdown? score = null)
    {
        Id = id;
        Name = name;
        Command = command;
        ExecutablePath = executablePath;
        Location = location;
        SourceType = sourceType;
        IsEnabled = isEnabled;
        AssociatedPid = associatedPid;
        Score = score;
    }
}
