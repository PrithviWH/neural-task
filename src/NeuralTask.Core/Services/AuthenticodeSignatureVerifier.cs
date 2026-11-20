using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using NeuralTask.Core.Common;

namespace NeuralTask.Core.Services;

/// <summary>
/// Verifies PE file digital signatures locally using the native Windows WinVerifyTrust API
/// and CryptCATAdmin catalog signature verification.
/// Runs 100% offline with zero network CRL lookups per PRD §7 security requirements.
/// Evaluates both embedded Authenticode signatures and Windows Security Catalog signatures.
/// Strictly fails closed: never assumes an executable is signed based on its file path.
/// </summary>
public class AuthenticodeSignatureVerifier : ISignatureVerifier
{
    public static readonly AuthenticodeSignatureVerifier Instance = new();

    public const int DefaultCacheCapacity = 500;
    private readonly BoundedLruCache<string, SignatureCacheEntry> _cache;

    public int CacheCount => _cache.Count;
    public int CacheCapacity => _cache.MaxCapacity;

    public sealed record SignatureCacheEntry(SignatureStatus Status, DateTime LastWriteTimeUtc, long FileLength);

    public AuthenticodeSignatureVerifier(int cacheCapacity = DefaultCacheCapacity)
    {
        _cache = new BoundedLruCache<string, SignatureCacheEntry>(cacheCapacity, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;

    // WinVerifyTrust HRESULT return codes (standardized per WinError.h)
    private const int ERROR_SUCCESS = 0;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    private const int TRUST_E_ACTION_UNKNOWN = unchecked((int)0x800B0002);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_SUBJECT_NOT_TRUSTED = unchecked((int)0x800B0004);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010); // Standard WinError.h 0x80096010
    private const int TRUST_E_CERT_SIGNATURE = unchecked((int)0x80096004);
    private const int CERT_E_REVOKED = unchecked((int)0x800B010C);
    private const int CRYPT_E_REVOKED = unchecked((int)0x80092010);
    private const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int CERT_E_CHAINING = unchecked((int)0x800B010A);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CATALOG_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string wszCatalogFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszCatalogFilePath;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszMemberTag;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedHash;
        public uint cbCalculatedHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pChoiceData;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        ref WINTRUST_DATA pWVTData);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminAcquireContext(
        out IntPtr phCatAdmin,
        IntPtr pgSubsystem,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminReleaseContext(
        IntPtr hCatAdmin,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle(
        IntPtr hFile,
        ref uint pcbHash,
        [Out] byte[]? pbHash,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr hCatAdmin,
        byte[] pbHash,
        uint cbHash,
        uint dwFlags,
        ref IntPtr phPrevCatInfo);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminReleaseCatalogContext(
        IntPtr hCatAdmin,
        IntPtr hCatInfo,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptCATCatalogInfoFromContext(
        IntPtr hCatInfo,
        ref CATALOG_INFO psCatInfo,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    public SignatureStatus VerifySignature(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return SignatureStatus.Unknown;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return SignatureStatus.Unknown;
            }

            var lastWrite = File.GetLastWriteTimeUtc(filePath);
            var fileLength = new FileInfo(filePath).Length;

            if (_cache.TryGetValue(filePath, out var cached) && cached != null)
            {
                // Invalidate cache if file timestamp or length changed on disk
                if (cached.LastWriteTimeUtc == lastWrite && cached.FileLength == fileLength)
                {
                    return cached.Status;
                }
            }

            var status = VerifySignatureDirect(filePath);
            _cache.Set(filePath, new SignatureCacheEntry(status, lastWrite, fileLength));
            return status;
        }
        catch
        {
            return SignatureStatus.Unknown;
        }
    }

    private SignatureStatus VerifySignatureDirect(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return SignatureStatus.Unknown;
            }

            // Step 1: Attempt embedded PE Authenticode signature verification
            var embeddedResult = VerifyEmbeddedSignature(filePath);
            if (embeddedResult == SignatureStatus.Signed ||
                embeddedResult == SignatureStatus.InvalidOrRevoked)
            {
                return embeddedResult;
            }

            // Step 2: Attempt Windows Security Catalog signature verification
            // Many core Windows components (e.g. cmd.exe, drivers, system DLLs)
            // are catalog-signed rather than possessing embedded PE signatures.
            var catalogResult = VerifyCatalogSignature(filePath);
            if (catalogResult == SignatureStatus.Signed ||
                catalogResult == SignatureStatus.InvalidOrRevoked)
            {
                return catalogResult;
            }

            // Neither embedded nor catalog signature verified successfully
            return SignatureStatus.Unsigned;
        }
        catch
        {
            return SignatureStatus.Unknown;
        }
    }

    private SignatureStatus VerifyEmbeddedSignature(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            var winTrustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pChoiceData = pFileInfo,
                dwStateAction = WTD_STATEACTION_IGNORE,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };

            int hr = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref winTrustData);
            return EvaluateTrustHResult(hr);
        }
        finally
        {
            Marshal.FreeHGlobal(pFileInfo);
        }
    }

    private SignatureStatus VerifyCatalogSignature(string filePath)
    {
        IntPtr hFile = CreateFileW(
            filePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (hFile == IntPtr.Zero || hFile == (IntPtr)(-1))
        {
            return SignatureStatus.Unknown;
        }

        try
        {
            if (!CryptCATAdminAcquireContext(out var hCatAdmin, IntPtr.Zero, 0))
            {
                return SignatureStatus.Unknown;
            }

            try
            {
                uint cbHash = 0;
                CryptCATAdminCalcHashFromFileHandle(hFile, ref cbHash, null, 0);
                if (cbHash == 0)
                {
                    return SignatureStatus.Unsigned;
                }

                var pbHash = new byte[cbHash];
                if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref cbHash, pbHash, 0))
                {
                    return SignatureStatus.Unsigned;
                }

                string memberTag = BitConverter.ToString(pbHash).Replace("-", "");

                IntPtr pHash = Marshal.AllocHGlobal((int)cbHash);
                try
                {
                    Marshal.Copy(pbHash, 0, pHash, (int)cbHash);

                    IntPtr hPrevCatInfo = IntPtr.Zero;
                    IntPtr hCatInfo = IntPtr.Zero;
                    bool anyInvalidOrRevoked = false;

                    // Windows CryptCATAdmin catalog enumeration:
                    // Passing ref hPrevCatInfo causes CryptCATAdminEnumCatalogFromHash to advance and internally
                    // release the previous catalog context handle. When enumeration finishes (returns NULL),
                    // the last catalog context is released automatically by the Win32 function.
                    // If the loop terminates early (e.g. upon finding a valid signed catalog), the current active
                    // hCatInfo must be explicitly released via CryptCATAdminReleaseCatalogContext.
                    while ((hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, pbHash, cbHash, 0, ref hPrevCatInfo)) != IntPtr.Zero)
                    {
                        hPrevCatInfo = hCatInfo; // Advance pointer for next iteration

                        var catInfo = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>() };
                        if (!CryptCATCatalogInfoFromContext(hCatInfo, ref catInfo, 0))
                        {
                            continue;
                        }

                        var wtc = new WINTRUST_CATALOG_INFO
                        {
                            cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                            dwCatalogVersion = 0,
                            pcwszCatalogFilePath = catInfo.wszCatalogFile,
                            pcwszMemberTag = memberTag,
                            pcwszMemberFilePath = filePath,
                            hMemberFile = hFile,
                            pbCalculatedHash = pHash,
                            cbCalculatedHash = cbHash,
                            pcCatalogContext = IntPtr.Zero,
                            hCatAdmin = hCatAdmin
                        };

                        IntPtr pWtc = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
                        try
                        {
                            Marshal.StructureToPtr(wtc, pWtc, false);

                            var wtd = new WINTRUST_DATA
                            {
                                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                                pPolicyCallbackData = IntPtr.Zero,
                                pSIPClientData = IntPtr.Zero,
                                dwUIChoice = WTD_UI_NONE,
                                fdwRevocationChecks = WTD_REVOKE_NONE,
                                dwUnionChoice = WTD_CHOICE_CATALOG,
                                pChoiceData = pWtc,
                                dwStateAction = WTD_STATEACTION_IGNORE,
                                hWVTStateData = IntPtr.Zero,
                                pwszURLReference = IntPtr.Zero,
                                dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                                dwUIContext = 0,
                                pSignatureSettings = IntPtr.Zero
                            };

                            int hr = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref wtd);
                            var status = EvaluateTrustHResult(hr);

                            if (status == SignatureStatus.Signed)
                            {
                                // Release the currently active catalog context before returning
                                CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
                                return SignatureStatus.Signed;
                            }

                            if (status == SignatureStatus.InvalidOrRevoked)
                            {
                                anyInvalidOrRevoked = true;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pWtc);
                        }
                    }

                    if (anyInvalidOrRevoked)
                    {
                        return SignatureStatus.InvalidOrRevoked;
                    }

                    return SignatureStatus.Unsigned;
                }
                finally
                {
                    Marshal.FreeHGlobal(pHash);
                }
            }
            finally
            {
                CryptCATAdminReleaseContext(hCatAdmin, 0);
            }
        }
        finally
        {
            CloseHandle(hFile);
        }
    }

    private static SignatureStatus EvaluateTrustHResult(int hr)
    {
        if (hr == ERROR_SUCCESS)
        {
            return SignatureStatus.Signed;
        }

        if (hr == TRUST_E_BAD_DIGEST ||
            hr == CERT_E_REVOKED ||
            hr == CRYPT_E_REVOKED ||
            hr == CERT_E_EXPIRED ||
            hr == TRUST_E_CERT_SIGNATURE ||
            hr == TRUST_E_SUBJECT_NOT_TRUSTED)
        {
            return SignatureStatus.InvalidOrRevoked;
        }

        return SignatureStatus.Unsigned;
    }
}
