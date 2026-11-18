using System;
using System.Collections.Generic;
using System.Linq;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class HeuristicFlagEngineTests
{
    private class MockSignatureVerifier : ISignatureVerifier
    {
        private readonly Dictionary<string, SignatureStatus> _map = new(StringComparer.OrdinalIgnoreCase);
        public SignatureStatus DefaultStatus { get; set; } = SignatureStatus.Signed;

        public void SetStatus(string path, SignatureStatus status) => _map[path] = status;

        public SignatureStatus VerifySignature(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return SignatureStatus.Unknown;
            return _map.TryGetValue(filePath, out var s) ? s : DefaultStatus;
        }
    }

    private readonly MockSignatureVerifier _mockSignatures = new();
    private readonly HeuristicFlagEngine _engine;

    public HeuristicFlagEngineTests()
    {
        _engine = new HeuristicFlagEngine(_mockSignatures);
    }

    [Fact]
    public void TempFolderExecution_WithUnsignedBinary_FlagsBothTempAndUnsigned()
    {
        var tempExe = @"C:\Users\TestUser\AppData\Local\Temp\random_dropped_payload.exe";
        _mockSignatures.SetStatus(tempExe, SignatureStatus.Unsigned);

        var snapshot = new ProcessSnapshot(
            Pid: 4500,
            Name: "random_dropped_payload.exe",
            ParentPid: 1000,
            CpuPercent: 1.0,
            RamBytes: 15_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: tempExe,
            IsAccessDenied: false,
            HandleCount: 150
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.True(result.HasFlags);
        Assert.Contains(result.Flags, f => f.RuleId == "PATH_TEMP");
        Assert.Contains(result.Flags, f => f.RuleId == "SIG_UNSIGNED");
        Assert.All(result.Flags, f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
        Assert.Equal(HeuristicSeverity.Warning, result.MaxSeverity);
    }

    [Fact]
    public void DownloadsFolderExecution_FlagsDownloadsNotice()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsExe = System.IO.Path.Combine(userProfile, "Downloads", "installer_tool.exe");
        _mockSignatures.SetStatus(downloadsExe, SignatureStatus.Signed);

        var snapshot = new ProcessSnapshot(
            Pid: 5200,
            Name: "installer_tool.exe",
            ParentPid: 1000,
            CpuPercent: 0.5,
            RamBytes: 25_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: downloadsExe,
            IsAccessDenied: false,
            HandleCount: 200
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.True(result.HasFlags);
        Assert.Contains(result.Flags, f => f.RuleId == "PATH_DOWNLOADS");
        Assert.DoesNotContain(result.Flags, f => f.RuleId == "SIG_UNSIGNED");
    }

    [Fact]
    public void ConservativeMode_SuppressesNormalDeveloperToolsInAppData()
    {
        // Tech audience safeguard: VS Code, Python, npm, etc. in AppData\Local\Programs
        var vsCodePath = @"C:\Users\Developer\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        _mockSignatures.SetStatus(vsCodePath, SignatureStatus.Signed);

        var snapshot = new ProcessSnapshot(
            Pid: 6100,
            Name: "Code.exe",
            ParentPid: 1000,
            CpuPercent: 2.5,
            RamBytes: 300_000_000,
            DiskBytesPerSec: 1024,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: vsCodePath,
            IsAccessDenied: false,
            HandleCount: 800
        );

        _engine.Sensitivity = HeuristicSensitivity.Conservative;
        var result = _engine.EvaluateSingle(snapshot);

        Assert.False(result.HasFlags);
    }

    [Fact]
    public void TypoSquatting_Svch0st_FlagsSystemNameMimicry()
    {
        var snapshot = new ProcessSnapshot(
            Pid: 7777,
            Name: "svch0st.exe", // '0' instead of 'o'
            ParentPid: 1000,
            CpuPercent: 0.1,
            RamBytes: 10_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: @"C:\Tools\svch0st.exe",
            IsAccessDenied: false,
            HandleCount: 120
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.True(result.HasFlags);
        var flag = result.Flags.FirstOrDefault(f => f.RuleId == "MIMIC_TYPO");
        Assert.NotNull(flag);
        Assert.Contains("svchost.exe", flag.Reason);
        Assert.Equal(HeuristicSeverity.Warning, flag.Severity);
    }

    [Fact]
    public void SystemBinaryMasquerade_OutsideSystem32_FlagsWarning()
    {
        var snapshot = new ProcessSnapshot(
            Pid: 8888,
            Name: "svchost.exe",
            ParentPid: 1000,
            CpuPercent: 0.2,
            RamBytes: 12_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: @"C:\Users\Public\svchost.exe", // Fake location
            IsAccessDenied: false,
            HandleCount: 100
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.True(result.HasFlags);
        var flag = result.Flags.FirstOrDefault(f => f.RuleId == "MIMIC_MASQUERADE");
        Assert.NotNull(flag);
        Assert.Contains("matches a core Windows system binary", flag.Reason);
        Assert.Equal(HeuristicSeverity.Warning, flag.Severity);
    }

    [Fact]
    public void LegitimateSystemBinary_InsideSystem32_DoesNotFlagMimicry()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var realSystem32Exe = System.IO.Path.Combine(winDir, "System32", "svchost.exe");

        var snapshot = new ProcessSnapshot(
            Pid: 900,
            Name: "svchost.exe",
            ParentPid: 600,
            CpuPercent: 0.1,
            RamBytes: 25_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: realSystem32Exe,
            IsAccessDenied: false,
            HandleCount: 450
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.DoesNotContain(result.Flags, f => f.RuleId.StartsWith("MIMIC_"));
    }

    [Fact]
    public void StatisticallyHighHandleCount_FlagsOutlierWithPercentileReason()
    {
        // Population of 10 processes with normal handle counts (100 - 500)
        var population = new List<ProcessSnapshot>();
        for (int i = 1; i <= 9; i++)
        {
            population.Add(new ProcessSnapshot(
                Pid: 1000 + i,
                Name: $"normal_service_{i}.exe",
                ParentPid: 500,
                CpuPercent: 0.1,
                RamBytes: 30_000_000,
                DiskBytesPerSec: 0,
                NetworkBytesPerSec: 0,
                StartTime: DateTimeOffset.Now,
                ExecutablePath: $@"C:\Program Files\Service_{i}\svc.exe",
                IsAccessDenied: false,
                HandleCount: 100 * i
            ));
        }

        // Outlier process with 7,500 handles
        var outlier = new ProcessSnapshot(
            Pid: 9999,
            Name: "leaky_agent.exe",
            ParentPid: 500,
            CpuPercent: 1.2,
            RamBytes: 80_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: @"C:\Program Files\Agent\leaky_agent.exe",
            IsAccessDenied: false,
            HandleCount: 7500
        );
        population.Add(outlier);

        var results = _engine.Evaluate(population);

        Assert.True(results.ContainsKey(9999));
        var outlierResult = results[9999];
        Assert.True(outlierResult.HasFlags);

        var handleFlag = outlierResult.Flags.FirstOrDefault(f => f.RuleId == "STAT_HANDLES");
        Assert.NotNull(handleFlag);
        Assert.Contains("7,500 handles", handleFlag.Reason);
        Assert.Contains("95th percentile", handleFlag.Reason);
    }

    [Fact]
    public void InvalidSignature_FlagsRevokedOrInvalidCertificate()
    {
        var badSignedExe = @"C:\Program Files\CorruptApp\app.exe";
        _mockSignatures.SetStatus(badSignedExe, SignatureStatus.InvalidOrRevoked);

        var snapshot = new ProcessSnapshot(
            Pid: 3333,
            Name: "app.exe",
            ParentPid: 1000,
            CpuPercent: 0.1,
            RamBytes: 20_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: badSignedExe,
            IsAccessDenied: false,
            HandleCount: 100
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.True(result.HasFlags);
        var flag = result.Flags.FirstOrDefault(f => f.RuleId == "SIG_INVALID");
        Assert.NotNull(flag);
        Assert.Contains("invalid or revoked", flag.Reason);
    }

    [Fact]
    public void DeliberateSuspiciousCase_RenamedBinaryInTemp_FiresExplainableFlags_WhileEverydayDevToolsRemainClean()
    {
        // 1. Setup normal developer toolset (should remain completely clean under conservative profile)
        var devTools = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(101, "Code.exe", 1000, 1.2, 250_000_000, 100, 0, DateTimeOffset.Now,
                @"C:\Users\Developer\AppData\Local\Programs\Microsoft VS Code\Code.exe", false, 650),
            new ProcessSnapshot(102, "node.exe", 101, 3.4, 120_000_000, 50, 0, DateTimeOffset.Now,
                @"C:\Program Files\nodejs\node.exe", false, 320),
            new ProcessSnapshot(103, "git.exe", 101, 0.1, 40_000_000, 0, 0, DateTimeOffset.Now,
                @"C:\Program Files\Git\bin\git.exe", false, 110),
            new ProcessSnapshot(104, "cargo.exe", 1000, 0.5, 35_000_000, 0, 0, DateTimeOffset.Now,
                @"C:\Users\Developer\.cargo\bin\cargo.exe", false, 90),
            new ProcessSnapshot(105, "python.exe", 1000, 0.2, 50_000_000, 0, 0, DateTimeOffset.Now,
                @"C:\Users\Developer\AppData\Local\Programs\Python\Python311\python.exe", false, 140)
        };

        // Mark normal dev tools as signed or trusted
        foreach (var dev in devTools)
        {
            _mockSignatures.SetStatus(dev.ExecutablePath!, SignatureStatus.Signed);
        }

        // 2. Deliberately-constructed suspicious case: renamed copy placed in %TEMP% and unsigned
        var tempExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svch0st.exe");
        _mockSignatures.SetStatus(tempExe, SignatureStatus.Unsigned);

        var suspiciousProcess = new ProcessSnapshot(
            Pid: 6666,
            Name: "svch0st.exe",
            ParentPid: 1000,
            CpuPercent: 0.8,
            RamBytes: 18_000_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.Now,
            ExecutablePath: tempExe,
            IsAccessDenied: false,
            HandleCount: 150
        );

        var fullPopulation = new List<ProcessSnapshot>(devTools) { suspiciousProcess };

        _engine.Sensitivity = HeuristicSensitivity.Conservative;
        var results = _engine.Evaluate(fullPopulation);

        // Verify normal dev tools are NOT flagged
        foreach (var dev in devTools)
        {
            Assert.True(results.ContainsKey(dev.Pid));
            Assert.False(results[dev.Pid].HasFlags, $"Dev tool '{dev.Name}' should not trigger false-positive flags.");
        }

        // Verify suspicious process fires multiple specific explainable flags
        Assert.True(results.ContainsKey(6666));
        var suspiciousResult = results[6666];
        Assert.True(suspiciousResult.HasFlags);
        Assert.True(suspiciousResult.Flags.Count >= 2);

        // Verify specific explainable reasons exist
        Assert.Contains(suspiciousResult.Flags, f => f.RuleId == "PATH_TEMP" && f.Reason.Contains("temporary directory"));
        Assert.Contains(suspiciousResult.Flags, f => f.RuleId == "MIMIC_TYPO" && f.Reason.Contains("svchost.exe"));
        Assert.Contains(suspiciousResult.Flags, f => f.RuleId == "SIG_UNSIGNED" && f.Reason.Contains("not digitally signed"));
        Assert.All(suspiciousResult.Flags, f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
    }

    [Fact]
    public void ProtectedAccessDeniedProcess_IsNeverFlagged()
    {
        var snapshot = new ProcessSnapshot(
            Pid: 4,
            Name: "System",
            ParentPid: 0,
            CpuPercent: 0.5,
            RamBytes: 100_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: null,
            ExecutablePath: null,
            IsAccessDenied: true,
            HandleCount: 0
        );

        var result = _engine.EvaluateSingle(snapshot);

        Assert.False(result.HasFlags);
    }
}
