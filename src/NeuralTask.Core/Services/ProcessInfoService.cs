using System;
using System.Collections.Concurrent;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Interop;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Service for generating structured process telemetry reports (PRD §6.2 - Feature 2.3).
/// Computes SHA-256 hashes asynchronously with in-memory caching to avoid main-thread blocking.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ProcessInfoService : IProcessInfoService
{
    public static readonly ProcessInfoService Instance = new();

    private readonly ISignatureVerifier _signatureVerifier;
    private readonly ConcurrentDictionary<string, string> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    private const int ProcessCommandLineInformation = 60;

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    public ProcessInfoService(ISignatureVerifier? signatureVerifier = null)
    {
        _signatureVerifier = signatureVerifier ?? AuthenticodeSignatureVerifier.Instance;
    }

    public async Task<ProcessReport> GenerateReportAsync(ProcessSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        string? cmdLine;
        string hash;
        string signer;

        if (snapshot.Pid <= 4 || snapshot.IsAccessDenied || string.IsNullOrWhiteSpace(snapshot.ExecutablePath))
        {
            cmdLine = "[Protected System Process // Access Denied]";
            hash = "[Protected System Process // Access Denied]";
            signer = snapshot.Pid <= 4
                ? "Microsoft Windows NT Kernel"
                : "Protected System Process (Access Denied)";
        }
        else
        {
            // 1. Query Command Line (Native NT API with WMI fallback)
            cmdLine = GetCommandLine(snapshot.Pid);

            // 2. Compute SHA-256 Hash asynchronously (never blocks caller)
            hash = await ComputeFileHashAsync(snapshot.ExecutablePath, cancellationToken).ConfigureAwait(false);

            // 3. Extract Signer Details
            signer = GetSignerInfo(snapshot.ExecutablePath);
        }

        string formatted = ProcessReport.FormatReport(
            pid: snapshot.Pid,
            name: snapshot.Name,
            parentPid: snapshot.ParentPid,
            executablePath: snapshot.ExecutablePath,
            commandLine: cmdLine,
            sha256Hash: hash,
            signerInfo: signer,
            cpuPercent: snapshot.CpuPercent,
            ramBytes: snapshot.RamBytes,
            ioBytesPerSec: snapshot.DiskBytesPerSec,
            startTime: snapshot.StartTime
        );

        return new ProcessReport(
            Pid: snapshot.Pid,
            Name: snapshot.Name,
            ParentPid: snapshot.ParentPid,
            ExecutablePath: snapshot.ExecutablePath,
            CommandLine: cmdLine,
            Sha256Hash: hash,
            SignerInfo: signer,
            CpuPercent: snapshot.CpuPercent,
            RamBytes: snapshot.RamBytes,
            IoBytesPerSec: snapshot.DiskBytesPerSec,
            StartTime: snapshot.StartTime,
            FormattedText: formatted
        );
    }

    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "[Unavailable]";
        }

        if (_hashCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        return await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return "[File Not Found]";
                }

                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 128 * 1024,
                    useAsync: true);

                using var sha256 = SHA256.Create();
                byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
                string hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();

                _hashCache[filePath] = hashString;
                return hashString;
            }
            catch (UnauthorizedAccessException)
            {
                return "[Access Denied]";
            }
            catch (IOException)
            {
                return "[Locked by another process]";
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public string? GetCommandLine(int pid)
    {
        if (pid <= 4) return "[System Process]";

        // 1. Attempt fast Native NtQueryInformationProcess (Class 60)
        IntPtr hProcess = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
        {
            hProcess = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_INFORMATION, false, pid);
        }

        if (hProcess != IntPtr.Zero)
        {
            try
            {
                // Query required buffer size
                NtQueryInformationProcess(hProcess, ProcessCommandLineInformation, IntPtr.Zero, 0, out int returnLength);

                if (returnLength > 0)
                {
                    IntPtr pBuffer = Marshal.AllocHGlobal(returnLength);
                    try
                    {
                        int ntStatus = NtQueryInformationProcess(hProcess, ProcessCommandLineInformation, pBuffer, returnLength, out _);
                        if (ntStatus == 0) // STATUS_SUCCESS
                        {
                            var unicodeString = Marshal.PtrToStructure<UNICODE_STRING>(pBuffer);
                            if (unicodeString.Length > 0 && unicodeString.Buffer != IntPtr.Zero)
                            {
                                string cmd = Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2);
                                if (!string.IsNullOrWhiteSpace(cmd))
                                {
                                    return cmd;
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pBuffer);
                    }
                }
            }
            catch
            {
                // Fall through to WMI
            }
            finally
            {
                Win32Native.CloseHandle(hProcess);
            }
        }

        // 2. Fallback to WMI query
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var cmd = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    return cmd;
                }
            }
        }
        catch
        {
            // Suppress WMI errors
        }

        return "[Unavailable // Access Denied]";
    }

    public string GetSignerInfo(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return "[Unavailable / Protected Process]";
        }

        try
        {
            if (!File.Exists(executablePath))
            {
                return "[File Not Found]";
            }

            // 1. Attempt reading embedded Authenticode Certificate
            using var cert = X509Certificate.CreateFromSignedFile(executablePath);
            if (cert != null)
            {
                using var cert2 = new X509Certificate2(cert);
                string subject = cert2.GetNameInfo(X509NameType.SimpleName, false);
                string issuer = cert2.GetNameInfo(X509NameType.SimpleName, true);

                var status = _signatureVerifier.VerifySignature(executablePath);
                return status switch
                {
                    SignatureStatus.Signed => $"{subject} (Verified Authenticode - {issuer})",
                    SignatureStatus.InvalidOrRevoked => $"{subject} (INVALID / REVOKED CERTIFICATE)",
                    _ => $"{subject} (Untrusted Root / Self-Signed)"
                };
            }
        }
        catch (CryptographicException)
        {
            // Binary does not contain an embedded signature.
            // Check if catalog-signed Windows OS system component.
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir) && executablePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft Windows Component (Catalog Signed)";
            }
        }
        catch
        {
            // General exception fall through
        }

        return "Unsigned binary";
    }
}
