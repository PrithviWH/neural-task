using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Heuristic Flag Engine (PRD §8.4):
/// Transparent, local-only heuristic engine that flags processes worth a second look.
/// Every flag carries an explainable, human-readable reason string.
/// 
/// EXPLICIT DISCLAIMER (PRD §8.4 & §11):
/// This engine is explicitly NOT an antivirus or malware scanner. It performs local diagnostic
/// triage to highlight processes running from unusual locations, unsigned binaries, statistical
/// handle count outliers, or names resembling known system services.
/// </summary>
public class HeuristicFlagEngine : IHeuristicFlagEngine
{
    private readonly ISignatureVerifier _signatureVerifier;

    private static readonly HashSet<string> KnownCoreSystemBinaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe",
        "csrss.exe",
        "lsass.exe",
        "LsaIso.exe",
        "services.exe",
        "smss.exe",
        "winlogon.exe",
        "explorer.exe",
        "taskhostw.exe",
        "dwm.exe",
        "wininit.exe",
        "spoolsv.exe",
        "conhost.exe",
        "RuntimeBroker.exe",
        "sihost.exe",
        "SearchHost.exe",
        "ctfmon.exe",
        "fontdrvhost.exe",
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe",
        "SecurityHealthSystray.exe",
        "SecurityHealthService.exe",
        "smartscreen.exe"
    };

    private static readonly string[] DeveloperToolSubstrings = new[]
    {
        @"AppData\Local\Programs",
        @"AppData\Roaming\npm",
        @"AppData\Local\Microsoft\WindowsApps",
        @"\.cargo\bin",
        @"\.dotnet\tools",
        @"\scoop\apps",
        @"AppData\Local\JetBrains"
    };

    public HeuristicSensitivity Sensitivity { get; set; } = HeuristicSensitivity.Conservative;

    public HeuristicFlagEngine(ISignatureVerifier? signatureVerifier = null)
    {
        _signatureVerifier = signatureVerifier ?? AuthenticodeSignatureVerifier.Instance;
    }

    public IReadOnlyDictionary<int, ProcessHeuristicResult> Evaluate(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        if (snapshots == null || snapshots.Count == 0)
        {
            return new Dictionary<int, ProcessHeuristicResult>();
        }

        // =========================================================================
        // STATISTICAL BASELINE METHODOLOGY FOR HANDLE COUNTS (PRD §8.4):
        // 1. Gather all non-zero handle counts across the snapshot population.
        // 2. Compute the 95th percentile (P95) to dynamically reflect current system load.
        // 3. Apply an absolute conservative noise floor (3,500 handles in Conservative mode,
        //    2,500 in Standard mode) so normal applications are never flagged as outliers
        //    on systems with low overall handle density.
        // =========================================================================
        int handleThreshold = CalculateHandlePercentileThreshold(snapshots, 0.95);

        var results = new System.Collections.Concurrent.ConcurrentDictionary<int, ProcessHeuristicResult>();
        System.Threading.Tasks.Parallel.ForEach(snapshots, snapshot =>
        {
            results[snapshot.Pid] = EvaluateSingle(snapshot, handleThreshold);
        });

        return results;
    }

    public ProcessHeuristicResult EvaluateSingle(ProcessSnapshot snapshot, int handleCountPercentileThreshold = 3500)
    {
        if (snapshot.Pid <= 4 || snapshot.IsAccessDenied)
        {
            // PID 0 (Idle), PID 4 (System), and protected kernel processes are managed directly
            // by the Windows NT executive kernel; never flag as user-mode anomalies.
            return ProcessHeuristicResult.Clean;
        }

        var flags = new List<ProcessHeuristicFlag>();

        // -------------------------------------------------------------------------
        // RULE 1: Process Name Mimicking System Binary (PRD §8.4)
        // -------------------------------------------------------------------------
        EvaluateNameMimicry(snapshot, flags);

        // -------------------------------------------------------------------------
        // RULE 2: Executable Path Under %TEMP%, %APPDATA%, or Downloads (PRD §8.4)
        // -------------------------------------------------------------------------
        EvaluatePathAnomalies(snapshot, flags);

        // -------------------------------------------------------------------------
        // RULE 3: Digital Signature Status (PRD §8.4)
        // -------------------------------------------------------------------------
        EvaluateDigitalSignature(snapshot, flags);

        // -------------------------------------------------------------------------
        // RULE 4: Statistically High Handle Count (PRD §8.4)
        // -------------------------------------------------------------------------
        EvaluateHandleCount(snapshot, handleCountPercentileThreshold, flags);

        return new ProcessHeuristicResult(
            Pid: snapshot.Pid,
            ProcessName: snapshot.Name,
            Flags: flags
        );
    }

    private void EvaluateNameMimicry(ProcessSnapshot snapshot, List<ProcessHeuristicFlag> flags)
    {
        var procName = snapshot.Name;
        if (string.IsNullOrWhiteSpace(procName)) return;

        // Sub-rule A: Core system binary name executing outside System32 / Windows
        if (KnownCoreSystemBinaries.Contains(procName))
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ExecutablePath))
            {
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var system32 = Path.Combine(winDir, "System32");

                bool inSystemDir = snapshot.ExecutablePath.StartsWith(system32, StringComparison.OrdinalIgnoreCase) ||
                                   snapshot.ExecutablePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase);

                if (!inSystemDir)
                {
                    flags.Add(new ProcessHeuristicFlag(
                        RuleId: "MIMIC_MASQUERADE",
                        Title: "System Binary Masquerade",
                        Reason: $"Process name '{procName}' matches a core Windows system binary but executes outside System32 ({snapshot.ExecutablePath})",
                        Severity: HeuristicSeverity.Warning
                    ));
                }
            }
            return; // It IS a known system binary name; do not check typo-distance against other system binaries
        }

        // Sub-rule B: Typo-squatting / Levenshtein distance check (e.g. "svch0st.exe", "csrs.exe")
        foreach (var sysBinary in KnownCoreSystemBinaries)
        {
            if (procName.Equals(sysBinary, StringComparison.OrdinalIgnoreCase)) continue;

            int dist = ComputeLevenshteinDistance(procName, sysBinary);
            if (dist > 0 && dist <= 2)
            {
                flags.Add(new ProcessHeuristicFlag(
                    RuleId: "MIMIC_TYPO",
                    Title: "System Name Mimicry",
                    Reason: $"Process name '{procName}' closely mimics Windows system binary '{sysBinary}' (Levenshtein distance: {dist})",
                    Severity: HeuristicSeverity.Warning
                ));
                break; // One mimic flag is sufficient
            }
        }
    }

    private void EvaluatePathAnomalies(ProcessSnapshot snapshot, List<ProcessHeuristicFlag> flags)
    {
        var path = snapshot.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var tempPath = Path.GetTempPath().TrimEnd('\\');
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsPath = Path.Combine(userProfile, "Downloads");
        var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 1. Check Temp directory
        if (path.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\AppData\Local\Temp", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Windows\Temp", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(new ProcessHeuristicFlag(
                RuleId: "PATH_TEMP",
                Title: "Temporary Folder Execution",
                Reason: $"Process is executing from a temporary directory: {path}",
                Severity: HeuristicSeverity.Warning
            ));
            return;
        }

        // 2. Check Downloads directory
        if (!string.IsNullOrEmpty(downloadsPath) && path.StartsWith(downloadsPath, StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(new ProcessHeuristicFlag(
                RuleId: "PATH_DOWNLOADS",
                Title: "Downloads Folder Execution",
                Reason: $"Process is running directly from the user's Downloads folder without installation: {path}",
                Severity: HeuristicSeverity.Notice
            ));
            return;
        }

        // 3. Check AppData directory
        bool inRoaming = !string.IsNullOrEmpty(appDataRoaming) && path.StartsWith(appDataRoaming, StringComparison.OrdinalIgnoreCase);
        bool inLocal = !string.IsNullOrEmpty(appDataLocal) && path.StartsWith(appDataLocal, StringComparison.OrdinalIgnoreCase);

        if (inRoaming || inLocal)
        {
            // Conservative False-Positive Mitigation for Developer Tools (PRD §8.4 Constraints):
            // Normal developer tools installed under AppData\Local\Programs (VS Code, Python, etc.)
            // or package managers (npm, cargo, scoop) and signed applications are not flagged under Conservative mode.
            bool isStandardDevLocation = DeveloperToolSubstrings.Any(sub => path.Contains(sub, StringComparison.OrdinalIgnoreCase));

            if (Sensitivity == HeuristicSensitivity.Conservative)
            {
                if (isStandardDevLocation)
                {
                    return; // Suppress AppData flag on legitimate dev installations
                }

                // If signed, suppress AppData flag under Conservative sensitivity
                var sigStatus = _signatureVerifier.VerifySignature(path);
                if (sigStatus == SignatureStatus.Signed)
                {
                    return; // Legitimate signed software installed per-user
                }
            }

            if (Sensitivity == HeuristicSensitivity.Aggressive || !isStandardDevLocation)
            {
                flags.Add(new ProcessHeuristicFlag(
                    RuleId: "PATH_APPDATA",
                    Title: "AppData Folder Execution",
                    Reason: $"Process executable resides in user application data directory: {path}",
                    Severity: HeuristicSeverity.Notice
                ));
            }
        }
    }

    private void EvaluateDigitalSignature(ProcessSnapshot snapshot, List<ProcessHeuristicFlag> flags)
    {
        var path = snapshot.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var status = _signatureVerifier.VerifySignature(path);

        if (status == SignatureStatus.InvalidOrRevoked)
        {
            flags.Add(new ProcessHeuristicFlag(
                RuleId: "SIG_INVALID",
                Title: "Invalid Digital Signature",
                Reason: "Binary has an invalid or revoked digital signature certificate",
                Severity: HeuristicSeverity.Warning
            ));
        }
        else if (status == SignatureStatus.Unsigned)
        {
            // False-Positive Tuning for Unsigned Binaries (PRD §8.4 Constraints):
            // Under Conservative sensitivity:
            // An unsigned binary is only flagged if it is ALSO running from an untrusted location
            // (Temp, Downloads, AppData) or mimics a system binary. Unsigned tools in standard
            // Program Files or development workspaces are treated gently as informational.
            if (Sensitivity == HeuristicSensitivity.Conservative)
            {
                bool hasPathOrNameFlags = flags.Any(f => f.RuleId.StartsWith("PATH_") || f.RuleId.StartsWith("MIMIC_"));
                if (hasPathOrNameFlags)
                {
                    flags.Add(new ProcessHeuristicFlag(
                        RuleId: "SIG_UNSIGNED",
                        Title: "Unsigned Binary",
                        Reason: "Binary is not digitally signed and runs from an untrusted location",
                        Severity: HeuristicSeverity.Notice
                    ));
                }
            }
            else
            {
                flags.Add(new ProcessHeuristicFlag(
                    RuleId: "SIG_UNSIGNED",
                    Title: "Unsigned Binary",
                    Reason: "Binary is not digitally signed with a trusted Authenticode certificate",
                    Severity: HeuristicSeverity.Notice
                ));
            }
        }
    }

    private void EvaluateHandleCount(ProcessSnapshot snapshot, int percentileThreshold, List<ProcessHeuristicFlag> flags)
    {
        if (snapshot.HandleCount <= 0) return;

        // Core system binaries running from Windows or System32 manage handles for the entire OS;
        // their expected baseline is significantly higher than user-mode processes.
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        bool isWindowsSystem = !string.IsNullOrEmpty(snapshot.ExecutablePath) &&
                               !string.IsNullOrEmpty(winDir) &&
                               (snapshot.ExecutablePath.StartsWith(Path.Combine(winDir, "System32"), StringComparison.OrdinalIgnoreCase) ||
                                snapshot.ExecutablePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase));

        int absoluteFloor = Sensitivity switch
        {
            HeuristicSensitivity.Conservative => isWindowsSystem ? 15000 : 4000,
            HeuristicSensitivity.Standard => isWindowsSystem ? 10000 : 2500,
            _ => isWindowsSystem ? 8000 : 1500
        };

        int effectiveThreshold = Math.Max(absoluteFloor, percentileThreshold);

        if (snapshot.HandleCount >= effectiveThreshold)
        {
            flags.Add(new ProcessHeuristicFlag(
                RuleId: "STAT_HANDLES",
                Title: "High Handle Count",
                Reason: $"Process holds {snapshot.HandleCount:N0} handles, exceeding statistical 95th percentile baseline of {effectiveThreshold:N0}",
                Severity: HeuristicSeverity.Notice
            ));
        }
    }

    private int CalculateHandlePercentileThreshold(IReadOnlyList<ProcessSnapshot> snapshots, double percentile)
    {
        var handleCounts = snapshots
            .Where(s => !s.IsAccessDenied && s.HandleCount > 0)
            .Select(s => s.HandleCount)
            .OrderBy(h => h)
            .ToList();

        if (handleCounts.Count == 0)
        {
            return 3500;
        }

        int index = (int)Math.Ceiling(percentile * handleCounts.Count) - 1;
        index = Math.Clamp(index, 0, handleCounts.Count - 1);

        return handleCounts[index];
    }

    public static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.Equals(s, t, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[] v0 = new int[m + 1];
        int[] v1 = new int[m + 1];

        for (int j = 0; j <= m; j++) v0[j] = j;

        for (int i = 0; i < n; i++)
        {
            v1[0] = i + 1;
            char sChar = char.ToLowerInvariant(s[i]);

            for (int j = 0; j < m; j++)
            {
                char tChar = char.ToLowerInvariant(t[j]);
                int cost = sChar == tChar ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
            }

            Array.Copy(v1, v0, m + 1);
        }

        return v0[m];
    }
}
