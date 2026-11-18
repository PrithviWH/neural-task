using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using NeuralTask.Core.Interop;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// High-performance Win32 process data sampler using native snapshots and handle queries.
/// Avoids the managed overhead of System.Diagnostics.Process.GetProcesses().
/// </summary>
public class Win32ProcessDataSampler : IProcessDataSampler
{
    public static readonly Win32ProcessDataSampler Instance = new();

    public IReadOnlyList<RawProcessSample> CollectRawSamples()
    {
        var samples = new List<RawProcessSample>(256);

        var snapshotHandle = Win32Native.CreateToolhelp32Snapshot(Win32Native.TH32CS_SNAPPROCESS, 0);
        if (snapshotHandle == IntPtr.Zero || snapshotHandle == new IntPtr(-1))
        {
            return samples;
        }

        try
        {
            var entry = new Win32Native.PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf<Win32Native.PROCESSENTRY32>();

            if (!Win32Native.Process32First(snapshotHandle, ref entry))
            {
                return samples;
            }

            var pathBuffer = new StringBuilder(1024);
            var memCounters = new Win32Native.PROCESS_MEMORY_COUNTERS_EX();
            var memCountersSize = (uint)Marshal.SizeOf<Win32Native.PROCESS_MEMORY_COUNTERS_EX>();
            memCounters.cb = memCountersSize;

            do
            {
                var pid = (int)entry.th32ProcessID;
                var parentPid = (int)entry.th32ParentProcessID;
                var name = entry.szExeFile ?? string.Empty;

                // Attempt to open process with limited query rights (sufficient for times, memory, IO, and path)
                var processHandle = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

                if (processHandle == IntPtr.Zero)
                {
                    // Fall back to standard query information if limited fails
                    processHandle = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_INFORMATION, false, pid);
                }

                if (processHandle == IntPtr.Zero)
                {
                    // Protected or system process (e.g., Secure System, anti-cheat) where access is denied
                    samples.Add(new RawProcessSample(
                        Pid: pid,
                        Name: name,
                        ParentPid: parentPid,
                        KernelTimeTicks: 0,
                        UserTimeTicks: 0,
                        WorkingSetBytes: 0,
                        ReadTransferBytes: 0,
                        WriteTransferBytes: 0,
                        OtherTransferBytes: 0,
                        StartTime: null,
                        ExecutablePath: null,
                        IsAccessDenied: true));

                    continue;
                }

                try
                {
                    // Process Times (CPU & StartTime)
                    DateTimeOffset? startTime = null;
                    long kernelTimeTicks = 0;
                    long userTimeTicks = 0;

                    if (Win32Native.GetProcessTimes(processHandle, out var createTime, out _, out var kernelTime, out var userTime))
                    {
                        if (createTime > 0)
                        {
                            try
                            {
                                startTime = DateTimeOffset.FromFileTime(createTime);
                            }
                            catch
                            {
                                // In rare cases an invalid timestamp can occur
                            }
                        }
                        kernelTimeTicks = kernelTime;
                        userTimeTicks = userTime;
                    }

                    // Process Working Set (RAM)
                    long workingSetBytes = 0;
                    if (Win32Native.GetProcessMemoryInfo(processHandle, out memCounters, memCountersSize))
                    {
                        workingSetBytes = (long)memCounters.WorkingSetSize.ToUInt64();
                    }

                    // Process IO Counters (Disk & Network/Other)
                    ulong readBytes = 0;
                    ulong writeBytes = 0;
                    ulong otherBytes = 0;

                    if (Win32Native.GetProcessIoCounters(processHandle, out var ioCounters))
                    {
                        readBytes = ioCounters.ReadTransferCount;
                        writeBytes = ioCounters.WriteTransferCount;
                        otherBytes = ioCounters.OtherTransferCount;
                    }

                    // Process Handle Count
                    int handleCount = 0;
                    if (Win32Native.GetProcessHandleCount(processHandle, out var hCount))
                    {
                        handleCount = (int)hCount;
                    }

                    // Process Full Executable Path
                    string? exePath = null;
                    pathBuffer.Clear();
                    int pathSize = pathBuffer.Capacity;
                    if (Win32Native.QueryFullProcessImageName(processHandle, 0, pathBuffer, ref pathSize))
                    {
                        exePath = pathBuffer.ToString();
                    }

                    samples.Add(new RawProcessSample(
                        Pid: pid,
                        Name: name,
                        ParentPid: parentPid,
                        KernelTimeTicks: kernelTimeTicks,
                        UserTimeTicks: userTimeTicks,
                        WorkingSetBytes: workingSetBytes,
                        ReadTransferBytes: readBytes,
                        WriteTransferBytes: writeBytes,
                        OtherTransferBytes: otherBytes,
                        StartTime: startTime,
                        ExecutablePath: exePath,
                        IsAccessDenied: false,
                        HandleCount: handleCount));
                }
                finally
                {
                    Win32Native.CloseHandle(processHandle);
                }
            } while (Win32Native.Process32Next(snapshotHandle, ref entry));
        }
        finally
        {
            Win32Native.CloseHandle(snapshotHandle);
        }

        return samples;
    }
}
