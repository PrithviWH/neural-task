using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeuralTask.Core.Interop;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SystemMetricsService : ISystemMetricsService
{
    public static readonly SystemMetricsService Instance = new();

    private readonly ITimeSource _timeSource;
    private readonly object _syncLock = new();

    private long _prevIdleTicks;
    private long _prevKernelTicks;
    private long _prevUserTicks;

    private long _prevNetworkTicks;
    private ulong _prevBytesReceived;
    private ulong _prevBytesSent;

    private string? _cachedProcessorName;

    public SystemMetricsService(ITimeSource? timeSource = null)
    {
        _timeSource = timeSource ?? SystemTimeSource.Instance;
    }

    public SystemMetricsSnapshot CollectSnapshot(IReadOnlyList<ProcessSnapshot> processes)
    {
        lock (_syncLock)
        {
            var currentTicks = _timeSource.TimestampTicks;
            var ticksPerSec = _timeSource.TicksPerSecond;

            // 1. Genuine System-Wide CPU Utilization
            double cpuPercent = 0.0;
            if (Win32Native.GetSystemTimes(out var idleTicks, out var kernelTicks, out var userTicks))
            {
                if (_prevKernelTicks > 0)
                {
                    var dIdle = idleTicks - _prevIdleTicks;
                    var dKernel = kernelTicks - _prevKernelTicks;
                    var dUser = userTicks - _prevUserTicks;

                    var totalSystemTicks = dKernel + dUser;
                    var busyTicks = totalSystemTicks - dIdle;

                    if (totalSystemTicks > 0 && busyTicks >= 0)
                    {
                        var rawCpu = ((double)busyTicks / totalSystemTicks) * 100.0;
                        cpuPercent = Math.Clamp(Math.Round(rawCpu, 1), 0.0, 100.0);
                    }
                }

                _prevIdleTicks = idleTicks;
                _prevKernelTicks = kernelTicks;
                _prevUserTicks = userTicks;
            }

            // 2. Genuine System Memory Utilization
            long totalMem = 0;
            long availMem = 0;
            long usedMem = 0;
            double memPercent = 0.0;

            var memStatus = new Win32Native.MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf<Win32Native.MEMORYSTATUSEX>();
            if (Win32Native.GlobalMemoryStatusEx(ref memStatus))
            {
                totalMem = (long)memStatus.ullTotalPhys;
                availMem = (long)memStatus.ullAvailPhys;
                usedMem = Math.Max(0, totalMem - availMem);
                if (totalMem > 0)
                {
                    memPercent = Math.Round(((double)usedMem / totalMem) * 100.0, 1);
                }
            }

            // 3. System Process I/O Rate (Sum of active process file/disk transfer deltas)
            double processIoBytesPerSec = 0.0;
            foreach (var p in processes)
            {
                processIoBytesPerSec += p.DiskBytesPerSec;
            }

            // 4. Genuine System-Wide Network Throughput (RX & TX via IPv4 Statistics)
            double netReceivedPerSec = 0.0;
            double netSentPerSec = 0.0;

            try
            {
                ulong totalRx = 0;
                ulong totalTx = 0;

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    if (iface.OperationalStatus == OperationalStatus.Up &&
                        iface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        iface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        try
                        {
                            var stats = iface.GetIPv4Statistics();
                            totalRx += (ulong)stats.BytesReceived;
                            totalTx += (ulong)stats.BytesSent;
                        }
                        catch
                        {
                            // Some virtual interfaces may throw on stats query
                        }
                    }
                }

                if (_prevNetworkTicks > 0 && currentTicks > _prevNetworkTicks)
                {
                    var deltaSeconds = (currentTicks - _prevNetworkTicks) / ticksPerSec;
                    if (deltaSeconds > 0.001)
                    {
                        if (totalRx >= _prevBytesReceived)
                        {
                            netReceivedPerSec = Math.Round((totalRx - _prevBytesReceived) / deltaSeconds, 1);
                        }
                        if (totalTx >= _prevBytesSent)
                        {
                            netSentPerSec = Math.Round((totalTx - _prevBytesSent) / deltaSeconds, 1);
                        }
                    }
                }

                _prevNetworkTicks = currentTicks;
                _prevBytesReceived = totalRx;
                _prevBytesSent = totalTx;
            }
            catch
            {
                // Network stats query fallback
            }

            // 5. System Static Metadata
            if (_cachedProcessorName == null)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    _cachedProcessorName = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
                }
                catch
                {
                }

                _cachedProcessorName ??= Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? $"{Environment.ProcessorCount}-Core Processor";
            }

            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var osVersion = $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} (Build {Environment.OSVersion.Version.Build})";

            return new SystemMetricsSnapshot(
                CpuPercent: cpuPercent,
                TotalMemoryBytes: totalMem,
                UsedMemoryBytes: usedMem,
                AvailableMemoryBytes: availMem,
                MemoryPercent: memPercent,
                ProcessIoBytesPerSec: Math.Round(processIoBytesPerSec, 1),
                NetworkReceivedBytesPerSec: netReceivedPerSec,
                NetworkSentBytesPerSec: netSentPerSec,
                ProcessorName: _cachedProcessorName,
                LogicalCoreCount: Environment.ProcessorCount,
                OsVersion: osVersion,
                ProcessCount: processes.Count,
                Uptime: uptime
            );
        }
    }
}
