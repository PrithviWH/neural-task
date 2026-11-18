using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

public interface IStartupScoreEngine
{
    int BootWindowSeconds { get; set; }
    bool IsAdministrator { get; }
    Task<IReadOnlyList<StartupEntry>> EnumerateStartupEntriesAsync(IReadOnlyList<ProcessSnapshot>? liveSnapshots = null);
    StartupScoreBreakdown CalculateScore(string entryName, string? executablePath, ProcessSnapshot? liveProcess, TimeSpan? systemUptime = null);
    bool SetEntryEnabled(StartupEntry entry, bool enable);
}
