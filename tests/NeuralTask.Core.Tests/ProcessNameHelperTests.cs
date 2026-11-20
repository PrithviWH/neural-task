using System;
using System.Collections.Generic;
using NeuralTask.Core.Common;
using NeuralTask.Core.Models;
using NeuralTask.Plugins.Gpu;
using Xunit;

namespace NeuralTask.Core.Tests;

public class ProcessNameHelperTests
{
    [Theory]
    [InlineData("chrome", "chrome")]
    [InlineData("chrome.exe", "chrome.exe")]
    [InlineData("chrome.exe.exe", "chrome.exe")]
    [InlineData("chrome.EXE.exe", "chrome.EXE")]
    [InlineData("chrome.exe.EXE", "chrome.exe")]
    [InlineData("my.tool.v1.0.exe", "my.tool.v1.0.exe")]
    [InlineData("  chrome.exe  ", "chrome.exe")]
    [InlineData("System", "System")]
    [InlineData("System / Kernel", "System / Kernel")]
    [InlineData("PID 1234", "PID 1234")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\t\r\n", "")]
    public void NormalizeProcessName_MatchesSpecificationExamples(string? input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);

        // Verify Normalize alias produces identical result
        Assert.Equal(expected, ProcessNameHelper.Normalize(input));
    }

    [Theory]
    [InlineData("app.exe.exe.exe.exe", "app.exe")]
    [InlineData("render.EXE.exe.exe", "render.EXE")]
    [InlineData("blender.exe.EXE.exe", "blender.exe")]
    [InlineData(".exe.exe", ".exe")]
    [InlineData(".exe", ".exe")]
    public void NormalizeProcessName_CollapsesMultipleRepeatedSuffixes(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("my.complex.app.v2.1.exe", "my.complex.app.v2.1.exe")]
    [InlineData("my.complex.app.v2.1.exe.exe", "my.complex.app.v2.1.exe")]
    [InlineData("dotnet.core.tool", "dotnet.core.tool")]
    [InlineData("script.py", "script.py")]
    [InlineData("archive.tar.gz", "archive.tar.gz")]
    public void NormalizeProcessName_PreservesInternalDotsAndStems(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("DISCORD.EXE", "DISCORD.EXE")]
    [InlineData("Steam.Exe", "Steam.Exe")]
    [InlineData("devenv.EXE.exe", "devenv.EXE")]
    [InlineData("OBS64.exe.EXE", "OBS64.exe")]
    public void NormalizeProcessName_HandlesMixedCasing(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("   app.exe   ", "app.exe")]
    [InlineData("\t\tSystem / Kernel \t", "System / Kernel")]
    [InlineData("  PID 4096  ", "PID 4096")]
    [InlineData("  devenv  ", "devenv")]
    public void NormalizeProcessName_TrimsLeadingAndTrailingWhitespace(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("System", "System")]
    [InlineData("System / Kernel", "System / Kernel")]
    [InlineData("Idle", "Idle")]
    [InlineData("[System Process]", "[System Process]")]
    [InlineData("Registry", "Registry")]
    [InlineData("Memory Compression", "Memory Compression")]
    [InlineData("PID 0", "PID 0")]
    [InlineData("PID 4", "PID 4")]
    [InlineData("PID 9876", "PID 9876")]
    [InlineData("Process 100", "Process 100")]
    public void NormalizeProcessName_PreservesSystemPseudoProcessesAndPidFallbacks(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("executable_runner.exe", "executable_runner.exe")]
    [InlineData("exe_processor.exe", "exe_processor.exe")]
    [InlineData("my.exe.runner.exe", "my.exe.runner.exe")]
    [InlineData("exe", "exe")]
    public void NormalizeProcessName_DoesNotCorruptInternalExeSubstrings(string input, string expected)
    {
        var actual = ProcessNameHelper.NormalizeProcessName(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GpuProcessMetric_WithNormalizedName_MatchesPipelineExpectations()
    {
        // Pipeline A: Verify GpuProcessMetric created with normalized name
        var rawName = "chrome.exe.exe";
        var normalized = ProcessNameHelper.NormalizeProcessName(rawName);
        var metric = new GpuProcessMetric(1234, normalized, 25.5);

        Assert.Equal("chrome.exe", metric.Name);
        Assert.Equal(1234, metric.Pid);
        Assert.Equal(25.5, metric.GpuPercent);
    }

    [Fact]
    public void GpuPanelViewModel_ApplyMetrics_ReceivesNormalizedProcessNames()
    {
        // Pipeline B: Verify GpuPanelViewModel displaying GpuProcessItem with normalized names
        var rawNames = new[] { "render.exe.exe", "blender.EXE.exe", "System / Kernel", "PID 5678", "dwm" };
        var processItems = new List<GpuProcessItem>();

        int pid = 1000;
        foreach (var raw in rawNames)
        {
            var cleanName = ProcessNameHelper.NormalizeProcessName(raw);
            processItems.Add(new GpuProcessItem(
                Pid: pid++,
                Name: cleanName,
                Engine: "3D Raster",
                UtilizationPercent: 10.0,
                Mode: "DirectX Windowed Application"));
        }

        var metrics = new GpuMetrics(
            AdapterName: "Test GPU",
            UtilizationPercent: 50.0,
            DedicatedMemoryUsedBytes: 1024L * 1024 * 1024,
            DedicatedMemoryTotalBytes: 4096L * 1024 * 1024,
            SharedMemoryUsedBytes: 256L * 1024 * 1024,
            Engine3DPercent: 50.0,
            EngineVideoDecodePercent: 0,
            EngineCopyPercent: 0,
            EngineComputePercent: 0,
            Timestamp: DateTime.UtcNow,
            TopProcesses: processItems);

        var vm = new GpuPanelViewModel();
        vm.ApplyMetrics(metrics);

        Assert.Equal(5, vm.TopProcesses.Count);
        Assert.Equal("render.exe", vm.TopProcesses[0].Name);
        Assert.Equal("blender.EXE", vm.TopProcesses[1].Name);
        Assert.Equal("System / Kernel", vm.TopProcesses[2].Name);
        Assert.Equal("PID 5678", vm.TopProcesses[3].Name);
        Assert.Equal("dwm", vm.TopProcesses[4].Name);
    }
}
