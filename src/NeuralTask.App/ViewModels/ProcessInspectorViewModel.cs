using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

/// <summary>
/// Detailed inspection viewmodel for a selected process (PRD §6.2 - Feature 2.3).
/// Displays live resource metrics, publisher verification, sanitized arguments, and SHA-256 hashes.
/// </summary>
public class ProcessInspectorViewModel : ViewModelBase
{
    private readonly IProcessInfoService _processInfoService;
    private readonly ProcessItemViewModel _item;

    private string _commandLine = "Loading command-line arguments...";
    private string _publisher = "Querying signature...";
    private bool _isSigned;
    private string _sha256 = "Computing SHA-256...";
    private bool _isDetailsLoaded;

    public ProcessItemViewModel Item => _item;

    public int Pid => _item.Pid;
    public string Name => _item.Name;
    public int ParentPid => _item.ParentPid;
    public string ParentDisplay => _item.ParentPid > 0
        ? (!string.IsNullOrEmpty(_item.ParentName) ? $"{_item.ParentName} (PID {_item.ParentPid})" : $"PID {_item.ParentPid}")
        : "Root Process";

    public string ExecutablePath => _item.ExecutablePath ?? "—";
    public bool HasExecutablePath => !string.IsNullOrWhiteSpace(_item.ExecutablePath) && File.Exists(_item.ExecutablePath);

    public string CpuDisplay => _item.CpuDisplay;
    public double CpuPercent => _item.CpuPercent;
    public string RamDisplay => _item.RamDisplay;
    public double RamBarPercent => _item.RamBarPercent;
    public string IoDisplay => _item.IoDisplay;
    public string FormattedStartTime => _item.FormattedStartTime;
    public string UptimeDisplay => _item.UptimeDisplay;

    public ImageSource? Icon => _item.Icon;
    public bool HasIcon => _item.HasIcon;
    public bool IsSystemProcess => _item.IsSystemProcess;
    public bool IsCriticalSystemProcess => _item.IsCriticalSystemProcess;
    public bool CanTerminate => _item.CanTerminate;
    public int ChildrenCount => _item.ChildrenCount;

    public string StatusDisplay
    {
        get
        {
            if (_item.IsCriticalSystemProcess) return "Protected System Core";
            if (_item.IsSystemProcess) return "Windows System Service";
            if (_item.IsAccessDenied) return "Access Denied";
            return "Active / Running";
        }
    }

    public Brush StatusBrush => _item.IsCriticalSystemProcess
        ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)) // Amber
        : (_item.IsSystemProcess
            ? new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)) // Indigo
            : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))); // Emerald / Teal

    public string CommandLine
    {
        get => _commandLine;
        private set => SetProperty(ref _commandLine, value);
    }

    public string Publisher
    {
        get => _publisher;
        private set
        {
            if (SetProperty(ref _publisher, value))
            {
                OnPropertyChanged(nameof(CleanPublisher));
                OnPropertyChanged(nameof(SignatureBadgeText));
                OnPropertyChanged(nameof(SignatureBadgeForeground));
                OnPropertyChanged(nameof(SignatureBadgeBackground));
            }
        }
    }

    public bool IsSigned
    {
        get => _isSigned;
        private set
        {
            if (SetProperty(ref _isSigned, value))
            {
                OnPropertyChanged(nameof(SignatureBadgeText));
                OnPropertyChanged(nameof(SignatureBadgeForeground));
                OnPropertyChanged(nameof(SignatureBadgeBackground));
            }
        }
    }

    public string CleanPublisher
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_publisher) || _publisher.StartsWith("[") || _publisher.StartsWith("Querying"))
            {
                return _publisher;
            }

            int parenIdx = _publisher.IndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0)
            {
                return _publisher[..parenIdx];
            }
            return _publisher;
        }
    }

    public string SignatureBadgeText
    {
        get
        {
            if (_item.IsCriticalSystemProcess || _item.Pid <= 4) return "Windows Kernel Core";
            if (_publisher.Contains("Self-Signed", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Untrusted", StringComparison.OrdinalIgnoreCase))
            {
                return "Self-Signed / Untrusted";
            }
            if (_isSigned || _publisher.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return "Verified Digital Signature";
            }
            if (_publisher.Contains("Unsigned", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Unverified", StringComparison.OrdinalIgnoreCase))
            {
                return "Unsigned Binary";
            }
            return "Authenticode Check";
        }
    }

    public Brush SignatureBadgeForeground
    {
        get
        {
            if (_item.IsCriticalSystemProcess || _item.Pid <= 4) return new SolidColorBrush(Color.FromRgb(0x83, 0x89, 0xFF)); // Indigo
            if (_publisher.Contains("Self-Signed", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Untrusted", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(0xF0, 0xA9, 0x4E)); // Amber
            }
            if (_isSigned || _publisher.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xC6)); // Emerald / Cyan
            }
            return new SolidColorBrush(Color.FromRgb(0xA3, 0xA7, 0xBA)); // Slate / Secondary
        }
    }

    public Brush SignatureBadgeBackground
    {
        get
        {
            if (_item.IsCriticalSystemProcess || _item.Pid <= 4) return new SolidColorBrush(Color.FromArgb(0x22, 0x83, 0x89, 0xFF));
            if (_publisher.Contains("Self-Signed", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Untrusted", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromArgb(0x22, 0xF0, 0xA9, 0x4E));
            }
            if (_isSigned || _publisher.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
                _publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromArgb(0x22, 0x2D, 0xD4, 0xC6));
            }
            return new SolidColorBrush(Color.FromArgb(0x18, 0xA3, 0xA7, 0xBA));
        }
    }

    public string ShortStartTime => _item.ShortStartTime;

    public string FormattedDateOnly => _item.StartTime.HasValue
        ? _item.StartTime.Value.ToLocalTime().ToString("yyyy-MM-dd")
        : "—";

    public string Sha256Hash
    {
        get => _sha256;
        private set => SetProperty(ref _sha256, value);
    }

    public ICommand CopyPathCommand { get; }
    public ICommand CopyCommandLineCommand { get; }
    public ICommand CopySha256Command { get; }
    public ICommand OpenFolderCommand { get; }

    public event Action<string, bool>? RequestShowStatus;

    public ProcessInspectorViewModel(ProcessItemViewModel item, IProcessInfoService processInfoService)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _processInfoService = processInfoService ?? throw new ArgumentNullException(nameof(processInfoService));

        CopyPathCommand = new RelayCommand(CopyPath);
        CopyCommandLineCommand = new RelayCommand(CopyCommandLine);
        CopySha256Command = new RelayCommand(CopySha256);
        OpenFolderCommand = new RelayCommand(OpenContainingFolder);

        // Populate initial metadata
        LoadAsync();
    }

    private void CopyPath()
    {
        if (!string.IsNullOrWhiteSpace(_item.ExecutablePath))
        {
            System.Windows.Clipboard.SetText(_item.ExecutablePath);
            RequestShowStatus?.Invoke($"Copied executable path for {_item.Name}", false);
        }
    }

    private void CopyCommandLine()
    {
        if (!string.IsNullOrWhiteSpace(CommandLine) && CommandLine != "—")
        {
            System.Windows.Clipboard.SetText(CommandLine);
            RequestShowStatus?.Invoke($"Copied command-line arguments for {_item.Name}", false);
        }
    }

    private void CopySha256()
    {
        if (!string.IsNullOrWhiteSpace(Sha256Hash) && Sha256Hash != "—" && !Sha256Hash.StartsWith("["))
        {
            System.Windows.Clipboard.SetText(Sha256Hash);
            RequestShowStatus?.Invoke($"Copied SHA-256 hash for {_item.Name}", false);
        }
    }

    private void OpenContainingFolder()
    {
        if (string.IsNullOrWhiteSpace(_item.ExecutablePath) || !File.Exists(_item.ExecutablePath))
        {
            RequestShowStatus?.Invoke($"Cannot open location: Executable file not found on disk.", true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_item.ExecutablePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            RequestShowStatus?.Invoke($"Failed to open folder: {ex.Message}", true);
        }
    }

    public void RefreshMetrics()
    {
        OnPropertyChanged(nameof(CpuDisplay));
        OnPropertyChanged(nameof(CpuPercent));
        OnPropertyChanged(nameof(RamDisplay));
        OnPropertyChanged(nameof(RamBarPercent));
        OnPropertyChanged(nameof(IoDisplay));
        OnPropertyChanged(nameof(UptimeDisplay));
        OnPropertyChanged(nameof(ChildrenCount));
    }

    private void LoadAsync()
    {
        if (_isDetailsLoaded) return;
        _isDetailsLoaded = true;

        if (_item.Pid <= 4 || _item.IsAccessDenied || string.IsNullOrWhiteSpace(_item.ExecutablePath))
        {
            CommandLine = "[Protected System Process // Access Denied]";
            Publisher = _item.Pid <= 4 ? "Microsoft Windows NT Kernel" : "Protected Windows Service";
            IsSigned = true;
            Sha256Hash = "[Protected System Binary]";
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var snap = new ProcessSnapshot(
                    _item.Pid,
                    _item.Name,
                    _item.ParentPid,
                    _item.CpuPercent,
                    _item.RamBytes,
                    0, 0,
                    _item.StartTime,
                    _item.ExecutablePath,
                    _item.IsAccessDenied);

                var report = await _processInfoService.GenerateReportAsync(snap);

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    CommandLine = SanitizeCommandLine(report.CommandLine);
                    Publisher = string.IsNullOrWhiteSpace(report.SignerInfo) ? "Unsigned / Unverified" : report.SignerInfo;
                    IsSigned = report.SignerInfo.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
                               report.SignerInfo.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
                    Sha256Hash = string.IsNullOrWhiteSpace(report.Sha256Hash) ? "[Unavailable]" : report.Sha256Hash;
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    CommandLine = $"[Query Error: {ex.Message}]";
                    Publisher = "[Inspection Error]";
                    Sha256Hash = "[Unavailable]";
                });
            }
        });
    }

    /// <summary>
    /// Masks sensitive flags (e.g. passwords, tokens, api keys, bearer tokens) from command-line string
    /// to adhere strictly to PRD §6.2: "Do not expose sensitive command-line information unnecessarily."
    /// </summary>
    public static string SanitizeCommandLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";

        // Regex matches key=value or --flag value where key is token, password, secret, key, auth
        string masked = Regex.Replace(
            raw,
            @"(?i)(--?(?:token|password|passwd|secret|api[-_]?key|credential|auth)[=:\s]+)([""']?[^\s""']+[""']?)",
            "$1********");

        return masked;
    }
}
