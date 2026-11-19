using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NeuralTask.App.Services;
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
    private ProcessHeuristicResult? _heuristicResult;
    private ImageSource? _icon;
    private bool _iconAttempted;
    private int _depth;
    private bool _hasChildren;
    private int _childrenCount;
    private bool _isExpanded = true;
    private bool _isVisibleInTree = true;
    private string? _parentName;

    public int Pid { get; }
    public string Name { get; }
    public int ParentPid { get; }
    public string ParentPidDisplay => ParentPid > 0 ? ParentPid.ToString() : "—";
    public DateTimeOffset? StartTime { get; }
    public string? ExecutablePath { get; private set; }

    public List<ProcessItemViewModel> Children { get; } = new();

    public ICommand ToggleExpandCommand { get; }
    public Action? OnExpandStateChanged { get; set; }

    public ProcessHeuristicResult? HeuristicResult
    {
        get => _heuristicResult;
        private set
        {
            if (SetProperty(ref _heuristicResult, value))
            {
                OnPropertyChanged(nameof(HasFlags));
                OnPropertyChanged(nameof(FlagCount));
                OnPropertyChanged(nameof(FlagBadgeText));
                OnPropertyChanged(nameof(FlagTooltip));
                OnPropertyChanged(nameof(FlagBadgeBrush));
            }
        }
    }

    public bool HasFlags => _heuristicResult?.HasFlags ?? false;
    public int FlagCount => _heuristicResult?.FlagCount ?? 0;
    public string FlagBadgeText => _heuristicResult != null && _heuristicResult.HasFlags
        ? $"⚠ {_heuristicResult.FlagCount} {(_heuristicResult.FlagCount == 1 ? "FLAG" : "FLAGS")}"
        : string.Empty;

    public string FlagTooltip => _heuristicResult?.TooltipText ?? "No heuristic anomalies detected.";

    public Brush FlagBadgeBrush
    {
        get
        {
            if (_heuristicResult == null || !_heuristicResult.HasFlags)
            {
                return new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xB8));
            }

            return _heuristicResult.MaxSeverity switch
            {
                HeuristicSeverity.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00)), // Amber/Warning
                HeuristicSeverity.Notice => new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),  // Neon Cyan
                _ => new SolidColorBrush(Color.FromRgb(0x79, 0x28, 0xCA))                        // Violet
            };
        }
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set
        {
            if (SetProperty(ref _cpuPercent, value))
            {
                CpuDisplay = _isAccessDenied ? "—" : $"{_cpuPercent:F2}%";
                OnPropertyChanged(nameof(CpuBarPercent));
                OnPropertyChanged(nameof(IsHighCpu));
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
                OnPropertyChanged(nameof(RamBarPercent));
                OnPropertyChanged(nameof(IsHighRam));
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
                OnPropertyChanged(nameof(IsSystemProcess));
                OnPropertyChanged(nameof(IsCriticalSystemProcess));
                OnPropertyChanged(nameof(CanTerminate));
            }
        }
    }

    public ImageSource? Icon
    {
        get
        {
            if (_icon == null && !_iconAttempted)
            {
                _iconAttempted = true;
                _icon = ProcessIconCache.GetIcon(ExecutablePath, loaded =>
                {
                    _icon = loaded;
                    OnPropertyChanged(nameof(Icon));
                    OnPropertyChanged(nameof(HasIcon));
                });
            }
            return _icon;
        }
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon != null;

    public bool IsCriticalSystemProcess =>
        Pid <= 4 ||
        Name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("smss.exe", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("wininit.exe", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("services.exe", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("lsass.exe", StringComparison.OrdinalIgnoreCase);

    public bool IsSystemProcess =>
        IsCriticalSystemProcess ||
        _isAccessDenied ||
        (!string.IsNullOrWhiteSpace(ExecutablePath) &&
         (ExecutablePath.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
          ExecutablePath.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase))) ||
        Name.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase);

    public bool CanTerminate => !IsCriticalSystemProcess;

    public string SystemBadgeText => IsCriticalSystemProcess ? "Protected" : (IsSystemProcess ? "System" : "");

    // --- Resource Bars & Visual Highlights ---
    public double CpuBarPercent => Math.Min(100.0, Math.Max(0.0, _cpuPercent));

    public double RamBarPercent
    {
        get
        {
            // Normalize up to 4 GB for visual bar scale
            double mb = _ramBytes / (1024.0 * 1024.0);
            return Math.Min(100.0, Math.Max(2.0, (mb / 4096.0) * 100.0));
        }
    }

    public bool IsHighCpu => _cpuPercent >= 15.0;
    public bool IsHighRam => _ramBytes >= 1024L * 1024L * 1024L; // >= 1 GB

    // --- Hierarchy / Parent-Child Tree View ---
    public int Depth
    {
        get => _depth;
        set
        {
            if (SetProperty(ref _depth, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
                OnPropertyChanged(nameof(IndentMargin));
            }
        }
    }

    public double IndentWidth => _depth * 16.0;
    public Thickness IndentMargin => new Thickness(_depth * 16.0, 0, 0, 0);

    public bool HasChildren
    {
        get => _hasChildren;
        set => SetProperty(ref _hasChildren, value);
    }

    public int ChildrenCount
    {
        get => _childrenCount;
        set => SetProperty(ref _childrenCount, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnExpandStateChanged?.Invoke();
            }
        }
    }

    public bool IsVisibleInTree
    {
        get => _isVisibleInTree;
        set => SetProperty(ref _isVisibleInTree, value);
    }

    public string? ParentName
    {
        get => _parentName;
        set => SetProperty(ref _parentName, value);
    }

    public string FormattedStartTime => StartTime.HasValue
        ? StartTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "—";

    public string ShortStartTime => StartTime.HasValue
        ? StartTime.Value.ToLocalTime().ToString("HH:mm:ss")
        : "—";

    public string UptimeDisplay
    {
        get
        {
            if (!StartTime.HasValue) return "—";
            var elapsed = DateTimeOffset.UtcNow - StartTime.Value;
            if (elapsed.TotalDays >= 1)
            {
                return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m";
            }
            if (elapsed.TotalHours >= 1)
            {
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
            }
            return $"{elapsed.Minutes}m {elapsed.Seconds}s";
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

        ToggleExpandCommand = new RelayCommand(() =>
        {
            IsExpanded = !IsExpanded;
        });
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
            _iconAttempted = false; // Retry icon retrieval with new path
            OnPropertyChanged(nameof(ExecutablePath));
            OnPropertyChanged(nameof(Icon));
        }
    }

    public void UpdateHeuristics(ProcessHeuristicResult result)
    {
        HeuristicResult = result;
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
