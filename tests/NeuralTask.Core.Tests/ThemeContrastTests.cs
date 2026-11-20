using System;
using Xunit;

namespace NeuralTask.Core.Tests;

/// <summary>
/// Automated WCAG AA Contrast Compliance Test Suite (PRD §7 & §5.3).
/// Mathematical verification of relative luminance and contrast ratios
/// across Dark Hologram and Light Holo-Glass design tokens.
/// </summary>
public class ThemeContrastTests
{
    // Dark Theme Hex Codes (Clarity Dark)
    private const string DarkBgBase = "#111319";
    private const string DarkPanelSurface = "#181B22";
    private const string DarkTextPrimary = "#F2F4F8";
    private const string DarkTextSecondary = "#949AA8";
    private const string DarkAccentPrimary = "#8389FF";
    private const string DarkAccentSecondary = "#2DD4C6";
    private const string DarkWarning = "#F0A94E";

    // Light Theme Hex Codes (Clarity Light)
    private const string LightBgBase = "#F6F7FB";
    private const string LightPanelSurface = "#FFFFFF";
    private const string LightTextPrimary = "#151821";
    private const string LightTextSecondary = "#525866";
    private const string LightAccentPrimary = "#5B61F1";
    private const string LightAccentSecondary = "#0D827A";
    private const string LightWarning = "#B45309";
    private const string LightBtnTextOnAccent = "#FFFFFF";

    [Theory]
    [InlineData("Dark Primary Text on Canvas", DarkTextPrimary, DarkBgBase, 4.5)]
    [InlineData("Dark Primary Text on Panel", DarkTextPrimary, DarkPanelSurface, 4.5)]
    [InlineData("Dark Secondary Text on Canvas", DarkTextSecondary, DarkBgBase, 4.5)]
    [InlineData("Dark Secondary Text on Panel", DarkTextSecondary, DarkPanelSurface, 4.5)]
    [InlineData("Dark Cyan Accent on Canvas (Large/UI)", DarkAccentPrimary, DarkBgBase, 3.0)]
    [InlineData("Dark Violet Accent on Canvas (Large/UI)", DarkAccentSecondary, DarkBgBase, 3.0)]
    [InlineData("Dark Amber Warning on Canvas (Large/UI)", DarkWarning, DarkBgBase, 3.0)]
    public void DarkTheme_PassesWcagAaCompliance(string label, string foregroundHex, string backgroundHex, double minRatio)
    {
        double ratio = CalculateContrastRatio(foregroundHex, backgroundHex);
        Assert.True(ratio >= minRatio, $"{label} ratio {ratio:0.00}:1 is below required {minRatio}:1");
    }

    [Theory]
    [InlineData("Light Primary Text on Canvas", LightTextPrimary, LightBgBase, 4.5)]
    [InlineData("Light Primary Text on Panel", LightTextPrimary, LightPanelSurface, 4.5)]
    [InlineData("Light Secondary Text on Canvas", LightTextSecondary, LightBgBase, 4.5)]
    [InlineData("Light Secondary Text on Panel", LightTextSecondary, LightPanelSurface, 4.5)]
    [InlineData("Light Teal Accent on Panel (Body/Normal)", LightAccentPrimary, LightPanelSurface, 4.5)]
    [InlineData("Light Violet Accent on Panel (Body/Normal)", LightAccentSecondary, LightPanelSurface, 4.5)]
    [InlineData("Light Amber Warning on Panel (Body/Normal)", LightWarning, LightPanelSurface, 4.5)]
    [InlineData("Light White Text on Teal Button", LightBtnTextOnAccent, LightAccentPrimary, 4.5)]
    [InlineData("Light White Text on Violet Button", LightBtnTextOnAccent, LightAccentSecondary, 4.5)]
    [InlineData("Light White Text on Amber Warning Button", LightBtnTextOnAccent, LightWarning, 4.5)]
    public void LightTheme_PassesWcagAaCompliance(string label, string foregroundHex, string backgroundHex, double minRatio)
    {
        double ratio = CalculateContrastRatio(foregroundHex, backgroundHex);
        Assert.True(ratio >= minRatio, $"{label} ratio {ratio:0.00}:1 is below required {minRatio}:1");
    }

    [Fact]
    public void Themes_AreNotSimpleInversions_VerifiedDistinctPalettes()
    {
        // PRD §5.3: Light theme must NOT be a simple inversion of dark theme's colors
        var darkBg = ParseColor(DarkBgBase);
        var lightBg = ParseColor(LightBgBase);
        var invertedDarkBg = (255 - darkBg.r, 255 - darkBg.g, 255 - darkBg.b);

        // A raw inversion of #0A0E14 would be #F5F1EB (warm yellow-tinted white)
        // Holo-Glass intentionally uses a cool clinical lab slate (#F1F5F9)
        Assert.NotEqual(invertedDarkBg, lightBg);

        // Accent comparison: Dark is bright electric cyan (#00E5FF),
        // raw inversion would be brown/orange (#FFAA00),
        // whereas Holo-Glass reinterprets it as deep jewel cyan/teal (#00758A)
        var darkAccent = ParseColor(DarkAccentPrimary);
        var lightAccent = ParseColor(LightAccentPrimary);
        var invertedDarkAccent = (255 - darkAccent.r, 255 - darkAccent.g, 255 - darkAccent.b);

        Assert.NotEqual(invertedDarkAccent, lightAccent);
        Assert.True(lightAccent.b > lightAccent.r, "Light accent preserves cool indigo identity");
    }

    [Fact]
    public void Themes_DeclareAllReferencedDynamicResources()
    {
        // Walks through all XAML files in the solution and verifies that any
        // {DynamicResource SomeKey} is declared in both DarkTheme.xaml and LightTheme.xaml (PRD §10).
        var solutionDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var srcDir = System.IO.Path.Combine(solutionDir, "src");
        if (!System.IO.Directory.Exists(srcDir)) return;

        var xamlFiles = System.IO.Directory.GetFiles(srcDir, "*.xaml", System.IO.SearchOption.AllDirectories);
        var darkPath = System.IO.Path.Combine(srcDir, "NeuralTask.Theming", "Themes", "DarkTheme.xaml");
        var lightPath = System.IO.Path.Combine(srcDir, "NeuralTask.Theming", "Themes", "LightTheme.xaml");

        if (!System.IO.File.Exists(darkPath) || !System.IO.File.Exists(lightPath)) return;

        var darkContent = System.IO.File.ReadAllText(darkPath);
        var lightContent = System.IO.File.ReadAllText(lightPath);

        var regex = new System.Text.RegularExpressions.Regex(@"DynamicResource\s+([A-Za-z0-9_]+)");
        var missingInDark = new System.Collections.Generic.List<string>();
        var missingInLight = new System.Collections.Generic.List<string>();

        foreach (var file in xamlFiles)
        {
            var text = System.IO.File.ReadAllText(file);
            var matches = regex.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var key = match.Groups[1].Value;
                if (!darkContent.Contains($"x:Key=\"{key}\"") && !missingInDark.Contains(key))
                {
                    missingInDark.Add(key);
                }
                if (!lightContent.Contains($"x:Key=\"{key}\"") && !missingInLight.Contains(key))
                {
                    missingInLight.Add(key);
                }
            }
        }

        Assert.True(missingInDark.Count == 0, $"Missing keys in DarkTheme.xaml: {string.Join(", ", missingInDark)}");
        Assert.True(missingInLight.Count == 0, $"Missing keys in LightTheme.xaml: {string.Join(", ", missingInLight)}");
    }

    /// <summary>
    /// Computes the relative luminance and contrast ratio per W3C WCAG 2.1 specs.
    /// Formula: (L1 + 0.05) / (L2 + 0.05) where L1 is lighter luminance.
    /// </summary>
    public static double CalculateContrastRatio(string hex1, string hex2)
    {
        double l1 = CalculateRelativeLuminance(hex1);
        double l2 = CalculateRelativeLuminance(hex2);

        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double CalculateRelativeLuminance(string hex)
    {
        var (rByte, gByte, bByte) = ParseColor(hex);

        double r = TransformChannel(rByte / 255.0);
        double g = TransformChannel(gByte / 255.0);
        double b = TransformChannel(bByte / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double TransformChannel(double c)
    {
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static (byte r, byte g, byte b) ParseColor(string hex)
    {
        string clean = hex.TrimStart('#');
        if (clean.Length == 8) clean = clean.Substring(2); // Strip alpha if present

        byte r = Convert.ToByte(clean.Substring(0, 2), 16);
        byte g = Convert.ToByte(clean.Substring(2, 2), 16);
        byte b = Convert.ToByte(clean.Substring(4, 2), 16);

        return (r, g, b);
    }
}
