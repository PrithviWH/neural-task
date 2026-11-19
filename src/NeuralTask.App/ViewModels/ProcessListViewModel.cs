using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class ProcessListViewModel : ViewModelBase
{
    private readonly IProcessActionService _actionService;
    private readonly IHeuristicFlagEngine _heuristicEngine;
    private readonly IProcessInfoService _processInfoService;
    private readonly Dictionary<int, ProcessItemViewModel> _allProcessesMap = new();
    private readonly List<ProcessItemViewModel> _allProcessesList = new();
    private readonly Dictionary<int, ProcessHeuristicResult> _heuristicCache = new();
    private IReadOnlyList<ProcessSnapshot> _lastSnapshots = Array.Empty<ProcessSnapshot>();

    private string _searchText = string.Empty;
    private string _sortColumn = "CpuPercent";
    private bool _sortAscending = false;
    private ProcessItemViewModel? _selectedProcess;
    private int _totalCount;
    private int _filteredCount;

    private bool _isStatusBannerVisible;
    private string _statusBannerMessage = string.Empty;
    private bool _statusBannerIsError;

    private bool _isInspectorVisible = true;
    private ProcessInspectorViewModel? _inspectorDetails;
    private bool _isTreeViewMode;
    private string _selectedFilterCategory = "All";

    private bool _isPidColumnVisible = true;
    private bool _isCpuColumnVisible = true;
    private bool _isRamColumnVisible = true;
    private bool _isIoColumnVisible = true;
    private bool _isNetworkColumnVisible = true;
    private bool _isParentPidColumnVisible = true;
    private bool _isStartTimeColumnVisible = true;
    private bool _isColumnsPopupOpen = false;

    // --- Stable Sorting & Debouncing State ---
    private readonly Dictionary<int, int> _previousRowOrder = new();
    private DateTime _lastSortTime = DateTime.MinValue;
    private readonly TimeSpan _sortDebounceInterval = TimeSpan.FromSeconds(2.5);
    private bool _needsImmediateSort = true;
    private const double CpuNoiseThreshold = 0.25; // 0.25% CPU noise deadband
    private const long RamNoiseThreshold = 10 * 1024 * 1024; // 10 MB RAM noise deadband

    public ObservableCollection<ProcessItemViewModel> FilteredProcesses { get; } = new();

    public Func<ProcessSnapshot, IReadOnlyList<ProcessSnapshot>, Task<(bool confirmed, bool entireTree)>>? ConfirmKillTreeCallback { get; set; }
    public Func<ProcessSnapshot, Task<bool>>? ConfirmKillProcessCallback { get; set; }

    public event Action? RequestScannerSweep;

    public ICommand ReScanCommand { get; }
    public ICommand CopyProcessInfoCommand { get; }
    public ICommand ToggleInspectorCommand { get; }
    public ICommand CloseInspectorCommand { get; }
    public ICommand ToggleTreeViewCommand { get; }
    public ICommand SetFilterCategoryCommand { get; }
    public ICommand ToggleColumnsPopupCommand { get; }
    public ICommand EndTaskCommand { get; }
    public ICommand EndTreeCommand { get; }
    public ICommand OpenContainingFolderCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _needsImmediateSort = true;
                ApplyFilterAndSort();
            }
        }
    }

    public string SortColumn
    {
        get => _sortColumn;
        set
        {
            if (SetProperty(ref _sortColumn, value))
            {
                NotifySortGlyphs();
            }
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (SetProperty(ref _sortAscending, value))
            {
                NotifySortGlyphs();
            }
        }
    }

    public ProcessItemViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                OnPropertyChanged(nameof(HasSelectedProcess));
                OnPropertyChanged(nameof(CanTerminateSelected));
                OnPropertyChanged(nameof(CanTerminateTreeSelected));

                if (_selectedProcess != null)
                {
                    InspectorDetails = new ProcessInspectorViewModel(_selectedProcess, _processInfoService);
                    InspectorDetails.RequestShowStatus += (msg, isErr) => ShowStatus(msg, isErr);
                    IsInspectorVisible = true;
                }
                else
                {
                    InspectorDetails = null;
                }
            }
        }
    }

    public bool HasSelectedProcess => _selectedProcess != null;
    public bool CanTerminateSelected => _selectedProcess != null && _selectedProcess.CanTerminate;
    public bool CanTerminateTreeSelected => _selectedProcess != null && _selectedProcess.CanTerminate;

    public bool IsInspectorVisible
    {
        get => _isInspectorVisible;
        set => SetProperty(ref _isInspectorVisible, value);
    }

    public ProcessInspectorViewModel? InspectorDetails
    {
        get => _inspectorDetails;
        private set => SetProperty(ref _inspectorDetails, value);
    }

    public bool IsTreeViewMode
    {
        get => _isTreeViewMode;
        set
        {
            if (SetProperty(ref _isTreeViewMode, value))
            {
                OnPropertyChanged(nameof(ViewModeDisplay));
                _needsImmediateSort = true;
                ApplyFilterAndSort();
            }
        }
    }

    public string ViewModeDisplay => _isTreeViewMode ? "Tree" : "Flat";

    public string SelectedFilterCategory
    {
        get => _selectedFilterCategory;
        set
        {
            if (SetProperty(ref _selectedFilterCategory, value))
            {
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterApps));
                OnPropertyChanged(nameof(IsFilterSystem));
                OnPropertyChanged(nameof(IsFilterHighUsage));
                OnPropertyChanged(nameof(IsFilterAnomalies));
                _needsImmediateSort = true;
                ApplyFilterAndSort();
            }
        }
    }

    public bool IsFilterAll => _selectedFilterCategory == "All";
    public bool IsFilterApps => _selectedFilterCategory == "Apps";
    public bool IsFilterSystem => _selectedFilterCategory == "System";
    public bool IsFilterHighUsage => _selectedFilterCategory == "HighUsage";
    public bool IsFilterAnomalies => _selectedFilterCategory == "Anomalies";

    // --- Column Visibility Settings ---
    public bool IsPidColumnVisible
    {
        get => _isPidColumnVisible;
        set => SetProperty(ref _isPidColumnVisible, value);
    }

    public bool IsCpuColumnVisible
    {
        get => _isCpuColumnVisible;
        set => SetProperty(ref _isCpuColumnVisible, value);
    }

    public bool IsRamColumnVisible
    {
        get => _isRamColumnVisible;
        set => SetProperty(ref _isRamColumnVisible, value);
    }

    public bool IsIoColumnVisible
    {
        get => _isIoColumnVisible;
        set => SetProperty(ref _isIoColumnVisible, value);
    }

    public bool IsNetworkColumnVisible
    {
        get => _isNetworkColumnVisible;
        set => SetProperty(ref _isNetworkColumnVisible, value);
    }

    public bool IsParentPidColumnVisible
    {
        get => _isParentPidColumnVisible;
        set => SetProperty(ref _isParentPidColumnVisible, value);
    }

    public bool IsStartTimeColumnVisible
    {
        get => _isStartTimeColumnVisible;
        set => SetProperty(ref _isStartTimeColumnVisible, value);
    }

    public bool IsColumnsPopupOpen
    {
        get => _isColumnsPopupOpen;
        set => SetProperty(ref _isColumnsPopupOpen, value);
    }

    // --- Sort Glyphs ---
    public string NameSortGlyph => GetSortGlyph("Name");
    public string PidSortGlyph => GetSortGlyph("Pid");
    public string CpuSortGlyph => GetSortGlyph("CpuPercent");
    public string RamSortGlyph => GetSortGlyph("RamBytes");
    public string IoSortGlyph => GetSortGlyph("DiskBytesPerSec");
    public string ParentPidSortGlyph => GetSortGlyph("ParentPid");
    public string StartTimeSortGlyph => GetSortGlyph("StartTime");

    private string GetSortGlyph(string column)
    {
        if (!_sortColumn.Equals(column, StringComparison.OrdinalIgnoreCase)) return "";
        return _sortAscending ? " ▲" : " ▼";
    }

    private void NotifySortGlyphs()
    {
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(PidSortGlyph));
        OnPropertyChanged(nameof(CpuSortGlyph));
        OnPropertyChanged(nameof(RamSortGlyph));
        OnPropertyChanged(nameof(IoSortGlyph));
        OnPropertyChanged(nameof(ParentPidSortGlyph));
        OnPropertyChanged(nameof(StartTimeSortGlyph));
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(ProcessCountDisplay));
            }
        }
    }

    public int FilteredCount
    {
        get => _filteredCount;
        private set
        {
            if (SetProperty(ref _filteredCount, value))
            {
                OnPropertyChanged(nameof(ProcessCountDisplay));
            }
        }
    }

    public string ProcessCountDisplay =>
        string.IsNullOrWhiteSpace(_searchText) && _selectedFilterCategory == "All"
            ? $"ACTIVE: {_totalCount} PROCESSES"
            : $"FILTERED: {_filteredCount} OF {_totalCount} PROCESSES";

    public bool IsStatusBannerVisible
    {
        get => _isStatusBannerVisible;
        set => SetProperty(ref _isStatusBannerVisible, value);
    }

    public string StatusBannerMessage
    {
        get => _statusBannerMessage;
        set => SetProperty(ref _statusBannerMessage, value);
    }

    public bool StatusBannerIsError
    {
        get => _statusBannerIsError;
        set => SetProperty(ref _statusBannerIsError, value);
    }

    public int FlaggedCount => _allProcessesList.Count(p => p.HasFlags);

    public string FlaggedCountDisplay => $"TRIAGE: {FlaggedCount} ANOMALIES";

    public ProcessListViewModel(
        IProcessActionService? actionService = null,
        IHeuristicFlagEngine? heuristicEngine = null,
        IProcessInfoService? processInfoService = null)
    {
        _actionService = actionService ?? new ProcessActionService();
        _heuristicEngine = heuristicEngine ?? new HeuristicFlagEngine();
        _processInfoService = processInfoService ?? ProcessInfoService.Instance;

        ReScanCommand = new RelayCommand(TriggerReScan);
        CopyProcessInfoCommand = new RelayCommand(async () => await CopyProcessInfoAsync());
        ToggleInspectorCommand = new RelayCommand(() =>
        {
            IsInspectorVisible = !IsInspectorVisible;
            if (IsInspectorVisible && SelectedProcess == null && FilteredProcesses.Count > 0)
            {
                SelectedProcess = FilteredProcesses[0];
            }
        });
        CloseInspectorCommand = new RelayCommand(() => IsInspectorVisible = false);
        ToggleTreeViewCommand = new RelayCommand(() => IsTreeViewMode = !IsTreeViewMode);
        SetFilterCategoryCommand = new RelayCommand(param =>
        {
            if (param is string cat)
            {
                SelectedFilterCategory = cat;
            }
        });
        ToggleColumnsPopupCommand = new RelayCommand(() => IsColumnsPopupOpen = !IsColumnsPopupOpen);
        EndTaskCommand = new RelayCommand(async _ => await TerminateSelectedAsync(), _ => CanTerminateSelected);
        EndTreeCommand = new RelayCommand(async _ => await TerminateTreeSelectedAsync(), _ => CanTerminateTreeSelected);
        OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder);
    }

    public void OpenContainingFolder()
    {
        var item = SelectedProcess;
        if (item == null || string.IsNullOrWhiteSpace(item.ExecutablePath) || !File.Exists(item.ExecutablePath))
        {
            ShowStatus("Cannot open location: Executable path is unavailable or protected.", true);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.ExecutablePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to open folder: {ex.Message}", true);
        }
    }

    public async Task CopyProcessInfoAsync(ProcessItemViewModel? target = null)
    {
        var item = target ?? SelectedProcess;
        if (item == null)
        {
            ShowStatus("NO PROCESS SELECTED // SELECT A ROW TO COPY TELEMETRY", true);
            return;
        }

        var snapshot = _lastSnapshots.FirstOrDefault(s => s.Pid == item.Pid)
                       ?? new ProcessSnapshot(item.Pid, item.Name, item.ParentPid, item.CpuPercent, item.RamBytes, 0, 0, item.StartTime, item.ExecutablePath, false);

        ShowStatus($"COMPUTING SHA-256 AND TELEMETRY REPORT FOR {item.Name} (PID {item.Pid})...", false);

        try
        {
            var report = await _processInfoService.GenerateReportAsync(snapshot);

            System.Windows.Clipboard.SetText(report.FormattedText);

            string shortHash = report.Sha256Hash.Length >= 8 ? report.Sha256Hash[..8] : report.Sha256Hash;
            ShowStatus($"COPIED REPORT TO CLIPBOARD // {item.Name} [PID {item.Pid}] // SHA-256: {shortHash}...", false);
        }
        catch (Exception ex)
        {
            ShowStatus($"FAILED TO COPY TELEMETRY: {ex.Message}", true);
        }
    }

    public void TriggerReScan()
    {
        if (_lastSnapshots == null || _lastSnapshots.Count == 0) return;

        var results = _heuristicEngine.Evaluate(_lastSnapshots);
        _heuristicCache.Clear();

        int flagged = 0;
        foreach (var kvp in results)
        {
            _heuristicCache[kvp.Key] = kvp.Value;
            if (_allProcessesMap.TryGetValue(kvp.Key, out var item))
            {
                item.UpdateHeuristics(kvp.Value);
                if (kvp.Value.HasFlags) flagged++;
            }
        }

        OnPropertyChanged(nameof(FlaggedCount));
        OnPropertyChanged(nameof(FlaggedCountDisplay));

        ShowStatus($"HEURISTIC AUDIT COMPLETE // {flagged} ANOMALIES IDENTIFIED (NOT AN ANTIVIRUS)", false);
        RequestScannerSweep?.Invoke();
    }

    public void ShowStatus(string message, bool isError = false)
    {
        StatusBannerMessage = message;
        StatusBannerIsError = isError;
        IsStatusBannerVisible = true;
    }

    public void DismissStatus()
    {
        IsStatusBannerVisible = false;
        StatusBannerMessage = string.Empty;
    }

    public void SortBy(string column)
    {
        if (_sortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = column switch
            {
                "Name" => true,
                "Pid" => true,
                "ParentPid" => true,
                "StartTime" => true,
                _ => false // CPU, RAM, IO default to descending
            };
        }

        _needsImmediateSort = true;
        ApplyFilterAndSort();
    }

    public void ApplySnapshot(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        _lastSnapshots = snapshots;
        var incomingPids = new HashSet<int>(snapshots.Count);
        int? previouslySelectedPid = _selectedProcess?.Pid;
        bool listStructureChanged = false;
        bool hasSignificantChange = false;

        // 1. Update existing items in place & add new items
        foreach (var snap in snapshots)
        {
            incomingPids.Add(snap.Pid);

            if (_allProcessesMap.TryGetValue(snap.Pid, out var existing))
            {
                if (Math.Abs(existing.CpuPercent - snap.CpuPercent) >= 5.0)
                {
                    hasSignificantChange = true;
                }
                existing.UpdateFrom(snap);
            }
            else
            {
                listStructureChanged = true;
                var newItem = new ProcessItemViewModel(snap);
                newItem.OnExpandStateChanged = () =>
                {
                    _needsImmediateSort = true;
                    ApplyFilterAndSort();
                };

                if (_heuristicCache.TryGetValue(snap.Pid, out var cached))
                {
                    newItem.UpdateHeuristics(cached);
                }
                else
                {
                    _ = Task.Run(() =>
                    {
                        var eval = _heuristicEngine.EvaluateSingle(snap);
                        _heuristicCache[snap.Pid] = eval;
                        newItem.UpdateHeuristics(eval);
                    });
                }

                _allProcessesMap[snap.Pid] = newItem;
                _allProcessesList.Add(newItem);
            }
        }

        // 2. Remove terminated processes
        for (int i = _allProcessesList.Count - 1; i >= 0; i--)
        {
            var item = _allProcessesList[i];
            if (!incomingPids.Contains(item.Pid))
            {
                listStructureChanged = true;
                _allProcessesMap.Remove(item.Pid);
                _heuristicCache.Remove(item.Pid);
                _allProcessesList.RemoveAt(i);
                _previousRowOrder.Remove(item.Pid);
            }
        }

        TotalCount = _allProcessesList.Count;

        // 3. Update Parent Names and Child Hierarchies
        UpdateHierarchyRelations(snapshots);

        // 4. Stable Sort & Debounce Check
        bool timeToResort = (DateTime.UtcNow - _lastSortTime) >= _sortDebounceInterval;
        bool shouldResort = _needsImmediateSort || listStructureChanged || FilteredProcesses.Count == 0 || timeToResort || hasSignificantChange;

        if (shouldResort)
        {
            ApplyFilterAndSort();
        }

        // 5. Restore selection and refresh live metrics in Inspector
        if (previouslySelectedPid.HasValue && _allProcessesMap.TryGetValue(previouslySelectedPid.Value, out var selected))
        {
            _selectedProcess = selected;
            OnPropertyChanged(nameof(SelectedProcess));
            OnPropertyChanged(nameof(HasSelectedProcess));
            OnPropertyChanged(nameof(CanTerminateSelected));
            OnPropertyChanged(nameof(CanTerminateTreeSelected));
            InspectorDetails?.RefreshMetrics();
        }
    }

    private void UpdateHierarchyRelations(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        // Clear children
        foreach (var item in _allProcessesList)
        {
            item.Children.Clear();
            item.HasChildren = false;
            item.ChildrenCount = 0;
            item.Depth = 0;
            item.ParentName = null;
        }

        // Map parent names and populate child lists
        foreach (var item in _allProcessesList)
        {
            if (item.ParentPid > 0 && _allProcessesMap.TryGetValue(item.ParentPid, out var parentItem))
            {
                item.ParentName = parentItem.Name;
                parentItem.Children.Add(item);
                parentItem.HasChildren = true;
                parentItem.ChildrenCount = parentItem.Children.Count;
            }
        }
    }

    public async Task TerminateSelectedAsync()
    {
        if (_selectedProcess == null)
        {
            ShowStatus("Please select a target process from the matrix first.", isError: true);
            return;
        }

        int targetPid = _selectedProcess.Pid;
        string targetName = _selectedProcess.Name;

        if (_selectedProcess.IsCriticalSystemProcess)
        {
            ShowStatus($"Access Denied: Protected System process '{targetName}' (PID: {targetPid}) is critical to Windows operation and cannot be terminated.", isError: true);
            return;
        }

        // Check if process has descendants
        var tree = _actionService.BuildTree(_lastSnapshots);
        var descendantPids = tree.GetDescendantPids(targetPid);

        if (descendantPids.Count > 0 && ConfirmKillTreeCallback != null)
        {
            var rootSnap = _lastSnapshots.FirstOrDefault(s => s.Pid == targetPid)
                           ?? new ProcessSnapshot(targetPid, targetName, 0, 0, 0, 0, 0, null, null, false);
            var childSnaps = _lastSnapshots.Where(s => descendantPids.Contains(s.Pid)).ToList();

            var (confirmed, entireTree) = await ConfirmKillTreeCallback(rootSnap, childSnaps);
            if (!confirmed)
            {
                return;
            }

            if (entireTree)
            {
                var treeResult = await _actionService.TerminateTreeAsync(targetPid, _lastSnapshots);
                if (treeResult.OverallSuccess)
                {
                    ShowStatus($"[KILL-TREE COMPLETED] Successfully terminated {treeResult.ProcessResults.Count} processes in '{targetName}' topology.", isError: false);
                }
                else
                {
                    var failed = treeResult.ProcessResults.Where(r => r.Status != ProcessTerminationStatus.Success && r.Status != ProcessTerminationStatus.AlreadyExited).ToList();
                    ShowStatus($"[PARTIAL TERMINATION] Terminated {treeResult.ProcessResults.Count - failed.Count} processes. {failed.Count} protected processes could not be terminated.", isError: true);
                }
                return;
            }
        }

        // Single process termination confirmation
        if (ConfirmKillProcessCallback != null)
        {
            var targetSnap = _lastSnapshots.FirstOrDefault(s => s.Pid == targetPid)
                             ?? new ProcessSnapshot(targetPid, targetName, 0, 0, 0, 0, 0, null, null, false);
            bool confirmed = await ConfirmKillProcessCallback(targetSnap);
            if (!confirmed) return;
        }

        // Single process termination
        var result = await _actionService.TerminateProcessAsync(targetPid);
        if (result.Status == ProcessTerminationStatus.Success)
        {
            ShowStatus($"[SUCCESS] {result.Message}", isError: false);
        }
        else if (result.Status == ProcessTerminationStatus.AlreadyExited)
        {
            ShowStatus($"[INFO] {result.Message}", isError: false);
        }
        else
        {
            ShowStatus($"[TERMINATION FAILED] {result.Message}", isError: true);
        }
    }

    public async Task TerminateTreeSelectedAsync()
    {
        if (_selectedProcess == null)
        {
            ShowStatus("Please select a target process from the matrix first.", isError: true);
            return;
        }

        int targetPid = _selectedProcess.Pid;
        string targetName = _selectedProcess.Name;

        if (_selectedProcess.IsCriticalSystemProcess)
        {
            ShowStatus($"Access Denied: Protected System process '{targetName}' (PID: {targetPid}) cannot be terminated.", isError: true);
            return;
        }

        var tree = _actionService.BuildTree(_lastSnapshots);
        var descendantPids = tree.GetDescendantPids(targetPid);

        if (ConfirmKillTreeCallback != null)
        {
            var rootSnap = _lastSnapshots.FirstOrDefault(s => s.Pid == targetPid)
                           ?? new ProcessSnapshot(targetPid, targetName, 0, 0, 0, 0, 0, null, null, false);
            var childSnaps = _lastSnapshots.Where(s => descendantPids.Contains(s.Pid)).ToList();

            var (confirmed, entireTree) = await ConfirmKillTreeCallback(rootSnap, childSnaps);
            if (!confirmed) return;

            var treeResult = await _actionService.TerminateTreeAsync(targetPid, _lastSnapshots);
            if (treeResult.OverallSuccess)
            {
                ShowStatus($"[KILL-TREE COMPLETED] Successfully terminated {treeResult.ProcessResults.Count} processes in '{targetName}' topology.", isError: false);
            }
            else
            {
                var failed = treeResult.ProcessResults.Where(r => r.Status != ProcessTerminationStatus.Success && r.Status != ProcessTerminationStatus.AlreadyExited).ToList();
                ShowStatus($"[PARTIAL TERMINATION] Terminated {treeResult.ProcessResults.Count - failed.Count} processes. {failed.Count} protected processes could not be terminated.", isError: true);
            }
        }
        else
        {
            var treeResult = await _actionService.TerminateTreeAsync(targetPid, _lastSnapshots);
            ShowStatus($"Terminated {treeResult.ProcessResults.Count} processes in tree.", !treeResult.OverallSuccess);
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<ProcessItemViewModel> items = _allProcessesList;

        // 1. Category Filter
        items = _selectedFilterCategory switch
        {
            "Apps" => items.Where(p => !p.IsSystemProcess),
            "System" => items.Where(p => p.IsSystemProcess),
            "HighUsage" => items.Where(p => p.CpuPercent >= 5.0 || p.RamBytes >= 500 * 1024 * 1024),
            "Anomalies" => items.Where(p => p.HasFlags),
            _ => items
        };

        // 2. Search Filter
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            items = items.Where(p => MatchesFuzzy(p, query));
        }

        List<ProcessItemViewModel> resultList;

        if (_isTreeViewMode)
        {
            // Build hierarchical view:
            // Find root items in current filtered set
            var filteredSet = new HashSet<int>(items.Select(i => i.Pid));
            var roots = _allProcessesList.Where(p => p.ParentPid <= 0 || !_allProcessesMap.ContainsKey(p.ParentPid)).ToList();

            // Sort roots
            roots = SortItems(roots);

            var flattened = new List<ProcessItemViewModel>();
            foreach (var root in roots)
            {
                // Only include root if it or any of its descendants match the filter
                if (HasMatchingDescendant(root, filteredSet))
                {
                    FlattenHierarchy(root, 0, flattened, filteredSet);
                }
            }

            resultList = flattened;
        }
        else
        {
            // Flat List View
            foreach (var item in items)
            {
                item.Depth = 0;
            }
            resultList = SortItems(items);
        }

        FilteredCount = resultList.Count;

        // Synchronize with ObservableCollection efficiently
        SyncObservableCollection(FilteredProcesses, resultList);

        // Record positions for stable comparison tie-breaking
        _previousRowOrder.Clear();
        for (int i = 0; i < resultList.Count; i++)
        {
            _previousRowOrder[resultList[i].Pid] = i;
        }

        _lastSortTime = DateTime.UtcNow;
        _needsImmediateSort = false;
    }

    private static bool HasMatchingDescendant(ProcessItemViewModel node, HashSet<int> matchedPids)
    {
        if (matchedPids.Contains(node.Pid)) return true;
        foreach (var child in node.Children)
        {
            if (HasMatchingDescendant(child, matchedPids)) return true;
        }
        return false;
    }

    private static void FlattenHierarchy(ProcessItemViewModel node, int depth, List<ProcessItemViewModel> target, HashSet<int> matchedPids)
    {
        node.Depth = depth;
        target.Add(node);

        if (node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                if (HasMatchingDescendant(child, matchedPids))
                {
                    FlattenHierarchy(child, depth + 1, target, matchedPids);
                }
            }
        }
    }

    private List<ProcessItemViewModel> SortItems(IEnumerable<ProcessItemViewModel> items)
    {
        var list = items.ToList();

        list.Sort((a, b) =>
        {
            int cmp = _sortColumn switch
            {
                "Name" => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                "Pid" => a.Pid.CompareTo(b.Pid),
                "ParentPid" => a.ParentPid.CompareTo(b.ParentPid),
                "StartTime" => Nullable.Compare(a.StartTime, b.StartTime),
                "DiskBytesPerSec" => CompareWithDeadband(a.DiskBytesPerSec, b.DiskBytesPerSec, 10240.0), // 10 KB/s noise threshold
                "RamBytes" => CompareWithDeadband(a.RamBytes, b.RamBytes, RamNoiseThreshold),
                _ => CompareWithDeadband(a.CpuPercent, b.CpuPercent, CpuNoiseThreshold) // "CpuPercent"
            };

            // If primary values are tied or within noise deadband, preserve previous relative order!
            if (cmp == 0)
            {
                bool hasA = _previousRowOrder.TryGetValue(a.Pid, out int idxA);
                bool hasB = _previousRowOrder.TryGetValue(b.Pid, out int idxB);

                if (hasA && hasB)
                {
                    cmp = idxA.CompareTo(idxB);
                }
                else if (hasA)
                {
                    return -1; // existing row stays higher
                }
                else if (hasB)
                {
                    return 1;
                }
                else
                {
                    // Fallback tie-breaker: deterministic by Name then Pid
                    int nameCmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    cmp = nameCmp != 0 ? nameCmp : a.Pid.CompareTo(b.Pid);
                }
            }
            else
            {
                if (!_sortAscending)
                {
                    cmp = -cmp;
                }
            }

            return cmp;
        });

        return list;
    }

    private static int CompareWithDeadband(double a, double b, double threshold)
    {
        if (Math.Abs(a - b) < threshold)
        {
            return 0; // within noise deadband -> treat as equal
        }
        return a.CompareTo(b);
    }

    private static int CompareWithDeadband(long a, long b, long threshold)
    {
        if (Math.Abs(a - b) < threshold)
        {
            return 0; // within noise deadband -> treat as equal
        }
        return a.CompareTo(b);
    }

    private static bool MatchesFuzzy(ProcessItemViewModel item, string query)
    {
        if (item.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return FuzzyMatcher.IsMatch(item.Name, query);
    }

    private static void SyncObservableCollection<T>(ObservableCollection<T> target, IList<T> source)
    {
        if (target.Count == 0)
        {
            foreach (var item in source)
            {
                target.Add(item);
            }
            return;
        }

        var sourceSet = new HashSet<T>(source);

        // 1. Remove items no longer in source
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!sourceSet.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        // 2. Align items to match source order
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    int existingIdx = target.IndexOf(item);
                    if (existingIdx >= 0)
                    {
                        target.Move(existingIdx, i);
                    }
                    else
                    {
                        target.Insert(i, item);
                    }
                }
            }
            else
            {
                target.Add(item);
            }
        }
    }
}
