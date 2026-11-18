using System;
using NeuralTask.Core.Models;

namespace NeuralTask.App.ViewModels;

public class ProcessItemViewModel : ViewModelBase
{
    private double _cpuPercent;
    private long _ramBytes;
    private double _diskBytesPerSec;
    private bool _isAccessDenied;
    private string _cpuDisplay = "0.00%";
    private string _ramDisplay = "0.0 MB";
    private string _ioDisplay = "—";

    public int Pid { get; }
    public string Name { get; }
    public int ParentPid { get; }
    public DateTimeOffset? StartTime { get; }
    public string? ExecutablePath { get; private set; }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set
        {
            if (SetProperty(ref _cpuPercent, value))
            {
                CpuDisplay = _isAccessDenied ? "—" : $"{_cpuPercent:F2}%";
            }
        }
    }

    public string CpuDisplay
    {
        get => _cpuDisplay;
        private set => SetProperty(ref _cpuDisplay, value);
    }

    public long RamBytes
    {
        get => _ramBytes;
        private set
        {
            if (SetProperty(ref _ramBytes, value))
            {
                RamDisplay = FormatBytes(_ramBytes);
            }
        }
    }

    public string RamDisplay
    {
        get => _ramDisplay;
        private set => SetProperty(ref _ramDisplay, value);
    }

    public double DiskBytesPerSec
    {
        get => _diskBytesPerSec;
        private set
        {
            if (SetProperty(ref _diskBytesPerSec, value))
            {
                IoDisplay = _isAccessDenied || _diskBytesPerSec <= 0 ? "—" : FormatRate(_diskBytesPerSec);
            }
        }
    }

    public string IoDisplay
    {
        get => _ioDisplay;
        private set => SetProperty(ref _ioDisplay, value);
    }

    /// <summary>
    /// Per-process network throughput is unavailable without kernel ETW/NDIS driver.
    /// Strictly adhering to prompt requirement: Never substitute unrelated counters; display —.
    /// </summary>
    public string NetworkDisplay => "—";

    public bool IsAccessDenied
    {
        get => _isAccessDenied;
        private set
        {
            if (SetProperty(ref _isAccessDenied, value))
            {
                if (_isAccessDenied)
                {
                    CpuDisplay = "—";
                    IoDisplay = "—";
                }
            }
        }
    }

    public ProcessItemViewModel(ProcessSnapshot snapshot)
    {
        Pid = snapshot.Pid;
        Name = string.IsNullOrEmpty(snapshot.Name) ? $"PID {snapshot.Pid}" : snapshot.Name;
        ParentPid = snapshot.ParentPid;
        StartTime = snapshot.StartTime;
        ExecutablePath = snapshot.ExecutablePath;
        _isAccessDenied = snapshot.IsAccessDenied;

        _cpuPercent = snapshot.CpuPercent;
        _ramBytes = snapshot.RamBytes;
        _diskBytesPerSec = snapshot.DiskBytesPerSec;

        _cpuDisplay = _isAccessDenied ? "—" : $"{_cpuPercent:F2}%";
        _ramDisplay = FormatBytes(_ramBytes);
        _ioDisplay = _isAccessDenied || _diskBytesPerSec <= 0 ? "—" : FormatRate(_diskBytesPerSec);
    }

    public void UpdateFrom(ProcessSnapshot snapshot)
    {
        IsAccessDenied = snapshot.IsAccessDenied;
        CpuPercent = snapshot.CpuPercent;
        RamBytes = snapshot.RamBytes;
        DiskBytesPerSec = snapshot.DiskBytesPerSec;

        if (ExecutablePath == null && snapshot.ExecutablePath != null)
        {
            ExecutablePath = snapshot.ExecutablePath;
            OnPropertyChanged(nameof(ExecutablePath));
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024.0)
        {
            return $"{mb / 1024.0:F2} GB";
        }
        return $"{mb:F1} MB";
    }

    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "—";
        double kb = bytesPerSec / 1024.0;
        if (kb >= 1024.0)
        {
            return $"{kb / 1024.0:F2} MB/s";
        }
        return $"{kb:F1} KB/s";
    }
}
