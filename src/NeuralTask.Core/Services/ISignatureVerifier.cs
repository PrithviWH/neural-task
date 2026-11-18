using System;

namespace NeuralTask.Core.Services;

public enum SignatureStatus
{
    Signed,
    Unsigned,
    InvalidOrRevoked,
    Unknown
}

/// <summary>
/// Verifies whether an executable binary possesses a valid digital signature.
/// Decoupled as an interface to allow isolated unit testing with synthetic process data.
/// </summary>
public interface ISignatureVerifier
{
    SignatureStatus VerifySignature(string? filePath);
}
