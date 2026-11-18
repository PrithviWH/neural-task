using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Startup Score Engine (PRD §8.3 & §11 Fallback):
/// Enumerates startup entries via Registry and WMI, correlates them with boot timing and live process telemetry,
/// and produces a normalized 0-100 impact score with an explainable time vs. resource contribution breakdown.
/// </summary>
[SupportedOSPlatform("windows")]
public class StartupScoreEngine : IStartupScoreEngine
{
    private const string HkcuRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmWow64RunKeyPath = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    private const string HkcuApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string HklmApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static readonly byte[] EnabledBinaryPayload = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] DisabledBinaryPayload = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public int BootWindowSeconds { get; set; } = 45;

    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<IReadOnlyList<StartupEntry>> EnumerateStartupEntriesAsync(IReadOnlyList<ProcessSnapshot>? liveSnapshots = null)
    {
        return Task.Run<IReadOnlyList<StartupEntry>>(() =>
        {
            var entries = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);

            // 1. Enumerate HKCU Run Key
            EnumerateRegistryKey(Registry.CurrentUser, HkcuRunKeyPath, HkcuApprovedKeyPath, StartupSourceType.RegistryRunCurrentUser, entries);

            // 2. Enumerate HKLM Run Key
            EnumerateRegistryKey(Registry.LocalMachine, HklmRunKeyPath, HklmApprovedKeyPath, StartupSourceType.RegistryRunLocalMachine, entries);

            // 3. Enumerate HKLM WOW6432Node Run Key
            EnumerateRegistryKey(Registry.LocalMachine, HklmWow64RunKeyPath, HklmApprovedKeyPath, StartupSourceType.RegistryRunLocalMachine, entries);

            // 4. Enumerate User Startup Folder
            EnumerateStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSourceType.StartupFolderUser, entries);

            // 5. Enumerate Common Startup Folder
            EnumerateStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSourceType.StartupFolderCommon, entries);

            // 6. Supplement with WMI Win32_StartupCommand (PRD §8.1)
            EnumerateWmiStartupCommands(entries);

            // 7. Correlate with live processes and calculate explainable scores
            var resultList = new List<StartupEntry>(entries.Values);
            foreach (var entry in resultList)
            {
                ProcessSnapshot? matchedProcess = null;
                if (liveSnapshots != null && liveSnapshots.Count > 0)
                {
                    matchedProcess = FindMatchingProcess(entry, liveSnapshots);
                    if (matchedProcess != null)
                    {
                        entry.AssociatedPid = matchedProcess.Pid;
                    }
                }

                entry.Score = CalculateScore(entry.Name, entry.ExecutablePath, matchedProcess);
            }

            return resultList
                .OrderByDescending(e => e.Score?.TotalScore ?? 0)
                .ThenBy(e => e.Name)
                .ToList();
        });
    }

    public StartupScoreBreakdown CalculateScore(
        string entryName,
        string? executablePath,
        ProcessSnapshot? liveProcess,
        TimeSpan? systemUptime = null)
    {
        double windowSeconds = Math.Max(15, BootWindowSeconds);
        double activeDurationSeconds;
        double bootDelaySeconds;
        double cpuTimeSeconds;
        long ioBytes;

        if (liveProcess != null)
        {
            // Case 1: Active correlated process during logon / post-boot window
            var uptime = systemUptime ?? TimeSpan.FromMilliseconds(Environment.TickCount64);
            var bootTime = DateTime.Now - uptime;

            if (liveProcess.StartTime.HasValue && liveProcess.StartTime.Value >= bootTime)
            {
                bootDelaySeconds = Math.Max(0.0, (liveProcess.StartTime.Value - bootTime).TotalSeconds);
            }
            else
            {
                bootDelaySeconds = 2.0; // Early logon start
            }

            activeDurationSeconds = Math.Clamp(windowSeconds - (bootDelaySeconds > windowSeconds ? windowSeconds * 0.3 : bootDelaySeconds), 5.0, windowSeconds);

            // CPU time estimate based on process activity
            cpuTimeSeconds = Math.Round((liveProcess.CpuPercent / 100.0) * activeDurationSeconds + (liveProcess.RamBytes > 200_000_000 ? 1.5 : 0.5), 2);
            ioBytes = (long)Math.Max(liveProcess.DiskBytesPerSec * activeDurationSeconds, 1_500_000);
        }
        else
        {
            // Case 2: Offline / Not currently active - estimate based on binary footprint and complexity
            bootDelaySeconds = 5.0;
            long fileSizeBytes = 0;
            if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
            {
                try
                {
                    fileSizeBytes = new FileInfo(executablePath).Length;
                }
                catch
                {
                }
            }

            if (fileSizeBytes > 80_000_000)
            {
                // Heavy Electron / Chromium runtime package (e.g. Teams, Discord, Spotify)
                activeDurationSeconds = 28.0;
                cpuTimeSeconds = 3.8;
                ioBytes = 85_000_000;
            }
            else if (fileSizeBytes > 10_000_000)
            {
                activeDurationSeconds = 14.0;
                cpuTimeSeconds = 1.6;
                ioBytes = 25_000_000;
            }
            else
            {
                // Lightweight background utility / tray helper
                activeDurationSeconds = 4.0;
                cpuTimeSeconds = 0.4;
                ioBytes = 4_000_000;
            }
        }

        // =========================================================================
        // SCORING MATH & NORMALIZATION (PRD §8.3):
        // 1. Time Score (0 - 50 points):
        //    Impact of process initialization duration and boot-contention window.
        // =========================================================================
        double bootContentionMultiplier = bootDelaySeconds < 15.0 ? 1.25 : 1.0;
        double timeRatio = Math.Clamp(activeDurationSeconds / windowSeconds, 0.0, 1.0);
        double timeScore = Math.Clamp(timeRatio * 40.0 * bootContentionMultiplier, 0.0, 50.0);

        // =========================================================================
        // 2. Resource Score (0 - 50 points):
        //    CPU component (0 - 30) + Disk/File I/O component (0 - 20)
        // =========================================================================
        double cpuRatio = Math.Clamp(cpuTimeSeconds / (windowSeconds * 0.15), 0.0, 1.0);
        double cpuComponent = cpuRatio * 30.0;

        double ioRatio = Math.Clamp((double)ioBytes / (80.0 * 1024.0 * 1024.0), 0.0, 1.0);
        double ioComponent = ioRatio * 20.0;

        double resourceScore = Math.Clamp(cpuComponent + ioComponent, 0.0, 50.0);

        // =========================================================================
        // 3. Normalized Overall Impact Score (0 - 100)
        // =========================================================================
        int totalScore = (int)Math.Clamp(Math.Round(timeScore + resourceScore), 0, 100);

        string rating = totalScore switch
        {
            >= 80 => "Severe",
            >= 60 => "High",
            >= 40 => "Moderate",
            >= 20 => "Low",
            _ => "Minimal"
        };

        double ioMb = Math.Round(ioBytes / (1024.0 * 1024.0), 1);
        string summary = $"Active for {activeDurationSeconds:0.0}s during logon window ({bootDelaySeconds:0.0}s post-boot delay), consuming {cpuTimeSeconds:0.0}s CPU time and {ioMb} MB I/O.";

        return new StartupScoreBreakdown(
            TotalScore: totalScore,
            TimeScore: Math.Round(timeScore, 1),
            ResourceScore: Math.Round(resourceScore, 1),
            CpuTimeSeconds: Math.Round(cpuTimeSeconds, 2),
            IoBytes: ioBytes,
            ActiveDurationSeconds: Math.Round(activeDurationSeconds, 1),
            BootDelaySeconds: Math.Round(bootDelaySeconds, 1),
            ImpactRating: rating,
            Summary: summary
        );
    }

    public bool SetEntryEnabled(StartupEntry entry, bool enable)
    {
        try
        {
            if (entry.SourceType == StartupSourceType.RegistryRunCurrentUser)
            {
                using var key = Registry.CurrentUser.CreateSubKey(HkcuApprovedKeyPath, true);
                if (key != null)
                {
                    key.SetValue(entry.Name, enable ? EnabledBinaryPayload : DisabledBinaryPayload, RegistryValueKind.Binary);
                    entry.IsEnabled = enable;
                    return true;
                }
            }
            else if (entry.SourceType == StartupSourceType.RegistryRunLocalMachine)
            {
                if (!IsAdministrator)
                {
                    return false; // Elevated permissions required
                }

                using var key = Registry.LocalMachine.CreateSubKey(HklmApprovedKeyPath, true);
                if (key != null)
                {
                    key.SetValue(entry.Name, enable ? EnabledBinaryPayload : DisabledBinaryPayload, RegistryValueKind.Binary);
                    entry.IsEnabled = enable;
                    return true;
                }
            }
            else if (entry.SourceType == StartupSourceType.StartupFolderUser || entry.SourceType == StartupSourceType.StartupFolderCommon)
            {
                if (!string.IsNullOrEmpty(entry.Location) && File.Exists(entry.Location))
                {
                    if (!enable && !entry.Location.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var newPath = entry.Location + ".disabled";
                        File.Move(entry.Location, newPath);
                        entry.IsEnabled = false;
                        return true;
                    }
                    else if (enable && entry.Location.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var newPath = entry.Location.Substring(0, entry.Location.Length - ".disabled".Length);
                        File.Move(entry.Location, newPath);
                        entry.IsEnabled = true;
                        return true;
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void EnumerateRegistryKey(
        RegistryKey rootKey,
        string runPath,
        string approvedPath,
        StartupSourceType sourceType,
        Dictionary<string, StartupEntry> destination)
    {
        try
        {
            using var runKey = rootKey.OpenSubKey(runPath, false);
            if (runKey == null) return;

            using var approvedKey = rootKey.OpenSubKey(approvedPath, false);

            foreach (var valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;

                var command = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                var exePath = ExtractExecutablePath(command);

                bool isEnabled = true;
                if (approvedKey != null)
                {
                    if (approvedKey.GetValue(valueName) is byte[] binaryData && binaryData.Length > 0)
                    {
                        // 0x02 = Enabled, 0x03 or 0x01 = Disabled
                        isEnabled = (binaryData[0] == 0x02);
                    }
                }

                var id = $"{sourceType}_{valueName}";
                destination[id] = new StartupEntry(
                    id: id,
                    name: valueName,
                    command: command,
                    executablePath: exePath,
                    location: $@"{rootKey.Name}\{runPath}",
                    sourceType: sourceType,
                    isEnabled: isEnabled
                );
            }
        }
        catch
        {
        }
    }

    private void EnumerateStartupFolder(string folderPath, StartupSourceType sourceType, Dictionary<string, StartupEntry> destination)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        try
        {
            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                bool isEnabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var cleanName = isEnabled ? Path.GetFileNameWithoutExtension(file) : Path.GetFileNameWithoutExtension(fileName.Substring(0, fileName.Length - 9));

                var id = $"{sourceType}_{cleanName}";
                if (!destination.ContainsKey(id))
                {
                    destination[id] = new StartupEntry(
                        id: id,
                        name: cleanName,
                        command: file,
                        executablePath: file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? null : file,
                        location: folderPath,
                        sourceType: sourceType,
                        isEnabled: isEnabled
                    );
                }
            }
        }
        catch
        {
        }
    }

    private void EnumerateWmiStartupCommands(Dictionary<string, StartupEntry> destination)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Command, Description, Location, User FROM Win32_StartupCommand");
            using var collection = searcher.Get();

            foreach (ManagementObject item in collection)
            {
                var name = item["Caption"]?.ToString() ?? item["Description"]?.ToString() ?? string.Empty;
                var command = item["Command"]?.ToString() ?? string.Empty;
                var location = item["Location"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name)) continue;

                // Avoid duplicating entries already retrieved via direct Registry or Startup folders
                bool alreadyExists = destination.Values.Any(existing =>
                    existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(existing.Command) && existing.Command.Equals(command, StringComparison.OrdinalIgnoreCase)));

                if (alreadyExists) continue;

                var id = $"WMI_{location}_{name}";
                if (!destination.ContainsKey(id))
                {
                    destination[id] = new StartupEntry(
                        id: id,
                        name: name,
                        command: command,
                        executablePath: ExtractExecutablePath(command),
                        location: location,
                        sourceType: location.Contains("HKLM", StringComparison.OrdinalIgnoreCase)
                            ? StartupSourceType.RegistryRunLocalMachine
                            : StartupSourceType.RegistryRunCurrentUser,
                        isEnabled: true
                    );
                }
            }
        }
        catch
        {
        }
    }

    private static ProcessSnapshot? FindMatchingProcess(StartupEntry entry, IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var exeName = !string.IsNullOrEmpty(entry.ExecutablePath)
            ? Path.GetFileName(entry.ExecutablePath)
            : entry.Name;

        if (string.IsNullOrEmpty(exeName)) return null;

        var exeWithoutExt = Path.GetFileNameWithoutExtension(exeName);

        return snapshots.FirstOrDefault(p =>
            p.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(exeWithoutExt + ".exe", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(p.ExecutablePath) && !string.IsNullOrEmpty(entry.ExecutablePath) &&
             p.ExecutablePath.Equals(entry.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        );
    }

    private static string? ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        command = Environment.ExpandEnvironmentVariables(command.Trim());

        if (command.StartsWith("\""))
        {
            int closingQuote = command.IndexOf('\"', 1);
            if (closingQuote > 1)
            {
                return command.Substring(1, closingQuote - 1);
            }
        }

        int spaceIdx = command.IndexOf(' ');
        if (spaceIdx > 0)
        {
            return command.Substring(0, spaceIdx);
        }

        return command;
    }
}
