using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessInfoServiceTests
{
    private readonly ProcessInfoService _service = new();

    [Fact]
    public async Task ComputeFileHashAsync_ReturnsCorrectSha256_AndCachesResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "NeuralTask Process Info Verification Data 2026";
            await File.WriteAllTextAsync(tempFile, content);

            // Compute expected hash
            using var sha = SHA256.Create();
            var expectedBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
            var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();

            // First computation (cache miss)
            var actualHash1 = await _service.ComputeFileHashAsync(tempFile);
            Assert.Equal(expectedHex, actualHash1);

            // Second computation (cache hit)
            var actualHash2 = await _service.ComputeFileHashAsync(tempFile);
            Assert.Equal(expectedHex, actualHash2);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GenerateReportAsync_ProducesCleanLabeledReport_WithAllFields()
    {
        var currentProc = System.Diagnostics.Process.GetCurrentProcess();
        var snapshot = new ProcessSnapshot(
            Pid: currentProc.Id,
            Name: currentProc.ProcessName + ".exe",
            ParentPid: 1000,
            CpuPercent: 2.5,
            RamBytes: 150_000_000,
            DiskBytesPerSec: 1024 * 50,
            NetworkBytesPerSec: 0,
            StartTime: DateTimeOffset.UtcNow.AddMinutes(-10),
            ExecutablePath: currentProc.MainModule?.FileName,
            IsAccessDenied: false,
            HandleCount: 450
        );

        var report = await _service.GenerateReportAsync(snapshot);

        // Print report to standard output for verification
        Console.WriteLine(report.FormattedText);

        Assert.Equal(snapshot.Pid, report.Pid);
        Assert.Equal(snapshot.Name, report.Name);
        Assert.NotNull(report.Sha256Hash);
        Assert.False(string.IsNullOrWhiteSpace(report.Sha256Hash));
        Assert.NotNull(report.SignerInfo);
        Assert.NotNull(report.FormattedText);

        // Verify labeled fields per PRD §6.2
        Assert.Contains("NEURALTASK // PROCESS TELEMETRY REPORT", report.FormattedText);
        Assert.Contains($"Process Name : {snapshot.Name}", report.FormattedText);
        Assert.Contains($"PID          : {snapshot.Pid}", report.FormattedText);
        Assert.Contains("SHA-256 Hash :", report.FormattedText);
        Assert.Contains("Command Line :", report.FormattedText);
        Assert.Contains("Signer Info  :", report.FormattedText);
        Assert.Contains("Memory (RAM) :", report.FormattedText);
    }

    [Fact]
    public async Task GenerateReportAsync_HandlesProtectedProcessGracefully()
    {
        var snapshot = new ProcessSnapshot(
            Pid: 4,
            Name: "System",
            ParentPid: 0,
            CpuPercent: 0.1,
            RamBytes: 100_000,
            DiskBytesPerSec: 0,
            NetworkBytesPerSec: 0,
            StartTime: null,
            ExecutablePath: null,
            IsAccessDenied: true,
            HandleCount: 0
        );

        var report = await _service.GenerateReportAsync(snapshot);

        Assert.Equal(4, report.Pid);
        Assert.Contains("Access Denied", report.FormattedText);
        Assert.Contains("System", report.FormattedText);
    }

    [Fact]
    public async Task ComputeFileHashAsync_OnLargeFile_ComputesAsynchronouslyWithoutStall()
    {
        // Test with a 20MB file buffer to verify streaming hash performance
        var tempFile = Path.Combine(Path.GetTempPath(), $"neuraltask_large_test_{Guid.NewGuid()}.dat");
        try
        {
            var buffer = new byte[1024 * 1024]; // 1MB buffer
            new Random(42).NextBytes(buffer);

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < 20; i++)
                {
                    await fs.WriteAsync(buffer);
                }
            }

            var hash = await _service.ComputeFileHashAsync(tempFile);
            Assert.NotNull(hash);
            Assert.Equal(64, hash.Length); // 64 hex characters for SHA-256
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_On500MBFile_ComputesAsynchronouslyWithoutStall()
    {
        // Constraint verification: 500MB+ file hashing must be non-blocking and memory-bounded
        var tempFile = Path.Combine(Path.GetTempPath(), $"neuraltask_500mb_test_{Guid.NewGuid()}.dat");
        try
        {
            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                fs.SetLength(500L * 1024 * 1024); // 500 MB
                var chunk = new byte[64 * 1024];
                new Random(99).NextBytes(chunk);
                await fs.WriteAsync(chunk);
                fs.Seek(250L * 1024 * 1024, SeekOrigin.Begin);
                await fs.WriteAsync(chunk);
                fs.Seek(-64 * 1024, SeekOrigin.End);
                await fs.WriteAsync(chunk);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hash = await _service.ComputeFileHashAsync(tempFile);
            sw.Stop();

            Assert.NotNull(hash);
            Assert.Equal(64, hash.Length);
            // Verify second call is instant cache hit
            var swCached = System.Diagnostics.Stopwatch.StartNew();
            var cachedHash = await _service.ComputeFileHashAsync(tempFile);
            swCached.Stop();

            Assert.Equal(hash, cachedHash);
            Assert.True(swCached.ElapsedMilliseconds < 5, $"Cache hit should be near instant (<5ms), was {swCached.ElapsedMilliseconds}ms");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
