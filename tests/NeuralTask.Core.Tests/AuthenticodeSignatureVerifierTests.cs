using System;
using System.IO;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class AuthenticodeSignatureVerifierTests
{
    private readonly AuthenticodeSignatureVerifier _verifier = new();

    [Fact]
    public void VerifySignature_GenuinelySignedWindowsSystemBinary_ReturnsSigned()
    {
        // cmd.exe is a known catalog-signed Windows system binary
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(cmdPath), $"System binary not found at {cmdPath}");

        var status = _verifier.VerifySignature(cmdPath);

        Assert.Equal(SignatureStatus.Signed, status);
    }

    [Fact]
    public void VerifySignature_UnsignedBinaryOutsideWindows_ReturnsUnsigned()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"neuraltask_unsigned_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

            var status = _verifier.VerifySignature(tempFile);

            Assert.Equal(SignatureStatus.Unsigned, status);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void VerifySignature_UnsignedBinaryInWindowsSubdirectory_ReturnsUnsignedNotSigned()
    {
        // Tests that an unsigned binary located inside a Windows subdirectory (C:\Windows\Temp)
        // is NEVER granted Signed status simply because of its location.
        var winTempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        Assert.True(Directory.Exists(winTempDir), $"Windows Temp directory not found at {winTempDir}");

        var testFile = Path.Combine(winTempDir, $"neuraltask_test_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(testFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

            var status = _verifier.VerifySignature(testFile);

            // Crucial security requirement: must NOT be inferred as Signed just because it is inside Windows!
            Assert.NotEqual(SignatureStatus.Signed, status);
            Assert.Equal(SignatureStatus.Unsigned, status);
        }
        finally
        {
            if (File.Exists(testFile))
            {
                try { File.Delete(testFile); } catch { }
            }
        }
    }

    [Fact]
    public void VerifySignature_TamperedEmbeddedSignature_ReturnsInvalidOrRevoked()
    {
        // explorer.exe has a genuine embedded PE signature
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.True(File.Exists(explorerPath), $"System binary not found at {explorerPath}");

        var tempTampered = Path.Combine(Path.GetTempPath(), $"tampered_explorer_{Guid.NewGuid():N}.exe");
        try
        {
            var bytes = File.ReadAllBytes(explorerPath);
            // Flip a byte in the code section so the Authenticode digest no longer matches
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(tempTampered, bytes);

            var status = _verifier.VerifySignature(tempTampered);

            // Verifies TRUST_E_BAD_DIGEST (0x80096010) maps accurately to InvalidOrRevoked
            Assert.NotEqual(SignatureStatus.Signed, status);
            Assert.Equal(SignatureStatus.InvalidOrRevoked, status);
        }
        finally
        {
            if (File.Exists(tempTampered))
            {
                File.Delete(tempTampered);
            }
        }
    }

    [Fact]
    public void VerifySignature_TamperedCatalogBinary_ReturnsUnsigned()
    {
        // cmd.exe is catalog-signed; tampering changes its calculated hash so no catalog matches
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(cmdPath), $"System binary not found at {cmdPath}");

        var tempTampered = Path.Combine(Path.GetTempPath(), $"tampered_cmd_{Guid.NewGuid():N}.exe");
        try
        {
            var bytes = File.ReadAllBytes(cmdPath);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(tempTampered, bytes);

            var status = _verifier.VerifySignature(tempTampered);

            Assert.NotEqual(SignatureStatus.Signed, status);
            Assert.Equal(SignatureStatus.Unsigned, status);
        }
        finally
        {
            if (File.Exists(tempTampered))
            {
                File.Delete(tempTampered);
            }
        }
    }

    [Fact]
    public void VerifySignature_MissingOrInvalidFile_ReturnsUnknown()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.exe");

        var statusMissing = _verifier.VerifySignature(missingPath);
        Assert.Equal(SignatureStatus.Unknown, statusMissing);

        var statusNull = _verifier.VerifySignature(null);
        Assert.Equal(SignatureStatus.Unknown, statusNull);

        var statusEmpty = _verifier.VerifySignature(string.Empty);
        Assert.Equal(SignatureStatus.Unknown, statusEmpty);

        var statusWhitespace = _verifier.VerifySignature("   ");
        Assert.Equal(SignatureStatus.Unknown, statusWhitespace);
    }

    [Fact]
    public void VerifySignature_CorruptOrTamperedBinary_NeverReportsSigned()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"tampered_{Guid.NewGuid():N}.exe");
        try
        {
            // Pseudo PE header with garbage payload
            var bytes = new byte[4096];
            bytes[0] = 0x4D; // 'M'
            bytes[1] = 0x5A; // 'Z'
            new Random(42).NextBytes(bytes.AsSpan(2));
            File.WriteAllBytes(tempFile, bytes);

            var status = _verifier.VerifySignature(tempFile);

            Assert.NotEqual(SignatureStatus.Signed, status);
            Assert.Equal(SignatureStatus.Unsigned, status);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void VerifySignature_CachesResultsForRepeatedCalls()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var first = _verifier.VerifySignature(cmdPath);
        var second = _verifier.VerifySignature(cmdPath);

        Assert.Equal(first, second);
        Assert.Equal(SignatureStatus.Signed, second);
    }
}
