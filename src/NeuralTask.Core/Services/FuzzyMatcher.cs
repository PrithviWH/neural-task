using System;

namespace NeuralTask.Core.Services;

/// <summary>
/// High-performance, allocation-conscious fuzzy string matcher for process names and identifiers.
/// Matches query characters sequentially allowing gaps (subsequence matching), scoring consecutive hits.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Checks whether the pattern matches the target string as an in-order subsequence (allowing gaps).
    /// Case-insensitive. Empty or whitespace pattern matches everything.
    /// </summary>
    public static bool IsMatch(string? target, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        var targetSpan = target.AsSpan();
        var patternSpan = pattern.Trim().AsSpan();

        int pIdx = 0;
        int tIdx = 0;

        while (tIdx < targetSpan.Length && pIdx < patternSpan.Length)
        {
            char pChar = char.ToLowerInvariant(patternSpan[pIdx]);
            char tChar = char.ToLowerInvariant(targetSpan[tIdx]);

            if (pChar == tChar)
            {
                pIdx++;
            }

            tIdx++;
        }

        return pIdx == patternSpan.Length;
    }

    /// <summary>
    /// Checks whether the pattern matches the target string as an in-order subsequence,
    /// calculating a relevance score. Higher scores indicate tighter matches with consecutive characters.
    /// </summary>
    public static bool IsMatchWithScore(string? target, string? pattern, out int score)
    {
        score = 0;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            score = 100;
            return true;
        }

        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        var targetSpan = target.AsSpan();
        var patternSpan = pattern.Trim().AsSpan();

        int pIdx = 0;
        int consecutiveBonus = 0;
        int totalScore = 0;

        for (int tIdx = 0; tIdx < targetSpan.Length; tIdx++)
        {
            if (pIdx >= patternSpan.Length)
            {
                break;
            }

            char pChar = char.ToLowerInvariant(patternSpan[pIdx]);
            char tChar = char.ToLowerInvariant(targetSpan[tIdx]);

            if (pChar == tChar)
            {
                pIdx++;
                totalScore += 10 + (consecutiveBonus * 5);
                consecutiveBonus++;

                // Bonus for matching start of string or word boundary
                if (tIdx == 0 || targetSpan[tIdx - 1] == ' ' || targetSpan[tIdx - 1] == '.' || targetSpan[tIdx - 1] == '_' || targetSpan[tIdx - 1] == '-')
                {
                    totalScore += 15;
                }
            }
            else
            {
                consecutiveBonus = 0;
            }
        }

        if (pIdx == patternSpan.Length)
        {
            // Bonus for shorter overall length relative to match
            totalScore += Math.Max(0, 50 - targetSpan.Length);
            score = totalScore;
            return true;
        }

        score = 0;
        return false;
    }
}
