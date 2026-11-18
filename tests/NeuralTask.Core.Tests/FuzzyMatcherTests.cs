using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("chrome.exe", "chr", true)]
    [InlineData("chrome.exe", "ce", true)]
    [InlineData("chrome.exe", "crx", true)]
    [InlineData("chrome.exe", "exe", true)]
    [InlineData("NeuralTask.App.exe", "nt", true)]
    [InlineData("NeuralTask.App.exe", "neural", true)]
    [InlineData("NeuralTask.App.exe", "app", true)]
    [InlineData("svchost.exe", "svc", true)]
    [InlineData("svchost.exe", "host", true)]
    [InlineData("System", "sys", true)]
    [InlineData("System", "sm", true)]
    [InlineData("System", "system", true)]
    [InlineData("chrome.exe", "", true)]
    [InlineData("chrome.exe", "   ", true)]
    [InlineData(null, "chr", false)]
    [InlineData("chrome.exe", "xyz", false)]
    [InlineData("chrome.exe", "chromeeeee", false)]
    [InlineData("System", "syz", false)]
    public void IsMatch_EvaluatesCorrectly(string? target, string? pattern, bool expected)
    {
        var result = FuzzyMatcher.IsMatch(target, pattern);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsMatchWithScore_ConsecutiveMatchesScoreHigher()
    {
        // "chr" in "chrome.exe" has consecutive characters
        var match1 = FuzzyMatcher.IsMatchWithScore("chrome.exe", "chr", out int scoreConsecutive);
        
        // "ce" in "chrome.exe" has gap
        var match2 = FuzzyMatcher.IsMatchWithScore("chrome.exe", "ce", out int scoreWithGap);

        Assert.True(match1);
        Assert.True(match2);
        Assert.True(scoreConsecutive > scoreWithGap);
    }

    [Fact]
    public void IsMatch_CaseInsensitive()
    {
        Assert.True(FuzzyMatcher.IsMatch("NEURALTASK.EXE", "neural"));
        Assert.True(FuzzyMatcher.IsMatch("neuraltask.exe", "NEURAL"));
    }
}
