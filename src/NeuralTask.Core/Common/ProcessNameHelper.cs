using System;

namespace NeuralTask.Core.Common;

/// <summary>
/// Provides centralized normalization and sanitization for process executable names.
/// Prevents duplicated file extension suffixes (e.g., repeated '.exe') while preserving
/// valid stems, internal dots, extensionless names, and system pseudo-process identifiers.
/// </summary>
public static class ProcessNameHelper
{
    private const string ExeExtension = ".exe";

    /// <summary>
    /// Normalizes a process name by trimming whitespace and collapsing repeated trailing '.exe' suffixes
    /// case-insensitively until exactly one remains. Names without an '.exe' suffix, system pseudo-processes,
    /// and PID fallback labels are preserved as-is.
    /// </summary>
    /// <param name="rawName">The raw process name string.</param>
    /// <returns>The normalized process name, or an empty string if null, empty, or whitespace.</returns>
    public static string NormalizeProcessName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var name = rawName.Trim();
        if (name.Length == 0)
        {
            return string.Empty;
        }

        // Fast check: preserve system pseudo-processes and fallback labels
        if (IsPseudoProcess(name))
        {
            return name;
        }

        // Collapse repeated trailing .exe suffixes case-insensitively until exactly one remains
        while (name.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase))
        {
            var stemLength = name.Length - ExeExtension.Length;
            var candidateStem = name.Substring(0, stemLength).TrimEnd();

            if (candidateStem.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase))
            {
                name = candidateStem;
            }
            else
            {
                break;
            }
        }

        return name;
    }

    /// <summary>
    /// Normalizes a process name. Alias for <see cref="NormalizeProcessName(string?)"/>.
    /// </summary>
    /// <param name="rawName">The raw process name string.</param>
    /// <returns>The normalized process name, or an empty string if null, empty, or whitespace.</returns>
    public static string Normalize(string? rawName) => NormalizeProcessName(rawName);

    private static bool IsPseudoProcess(string name)
    {
        return name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("System / Kernel", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("[System Process]", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("PID ", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Process ", StringComparison.OrdinalIgnoreCase);
    }
}
