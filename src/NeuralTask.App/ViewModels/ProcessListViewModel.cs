using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

public class ProcessListViewModel : ViewModelBase
{
    private readonly IProcessActionService _actionService;
    private readonly IHeuristicFlagEngine _heuristicEngine;
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

    public ObservableCollection<ProcessItemViewModel> FilteredProcesses { get; } = new();

    public Func<ProcessSnapshot, IReadOnlyList<ProcessSnapshot>, Task<(bool confirmed, bool entireTree)>>? ConfirmKillTreeCallback { get; set; }

    public event Action? RequestScannerSweep;

    public System.Windows.Input.ICommand ReScanCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

    public string SortColumn
    {
        get => _sortColumn;
        set => SetProperty(ref _sortColumn, value);
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set => SetProperty(ref _sortAscending, value);
    }

    public ProcessItemViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set => SetProperty(ref _selectedProcess, value);
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
        string.IsNullOrWhiteSpace(_searchText)
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
        IHeuristicFlagEngine? heuristicEngine = null)
    {
        _actionService = actionService ?? new ProcessActionService();
        _heuristicEngine = heuristicEngine ?? new HeuristicFlagEngine();

        ReScanCommand = new RelayCommand(TriggerReScan);
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
                _ => false // CPU, RAM, IO default to descending
            };
        }

        ApplyFilterAndSort();
    }

    public void ApplySnapshot(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        _lastSnapshots = snapshots;
        var incomingPids = new HashSet<int>(snapshots.Count);
        int? previouslySelectedPid = _selectedProcess?.Pid;

        // 1. Update existing items in place & add new items
        foreach (var snap in snapshots)
        {
            incomingPids.Add(snap.Pid);

            if (_allProcessesMap.TryGetValue(snap.Pid, out var existing))
            {
                existing.UpdateFrom(snap);
            }
            else
            {
                var newItem = new ProcessItemViewModel(snap);
                if (_heuristicCache.TryGetValue(snap.Pid, out var cached))
                {
                    newItem.UpdateHeuristics(cached);
                }
                else
                {
                    var eval = _heuristicEngine.EvaluateSingle(snap);
                    _heuristicCache[snap.Pid] = eval;
                    newItem.UpdateHeuristics(eval);
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
                _allProcessesMap.Remove(item.Pid);
                _heuristicCache.Remove(item.Pid);
                _allProcessesList.RemoveAt(i);
            }
        }

        TotalCount = _allProcessesList.Count;

        // 3. Apply active search filter and sort
        ApplyFilterAndSort();

        // 4. Restore selection if process is still alive
        if (previouslySelectedPid.HasValue && _allProcessesMap.TryGetValue(previouslySelectedPid.Value, out var selected))
        {
            SelectedProcess = selected;
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

        if (targetPid <= 4)
        {
            ShowStatus($"Access Denied: Protected System process '{targetName}' (PID: {targetPid}) cannot be terminated.", isError: true);
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

    private void ApplyFilterAndSort()
    {
        IEnumerable<ProcessItemViewModel> items = _allProcessesList;

        // Centralized Fuzzy Filter
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            items = items.Where(p => MatchesFuzzy(p, query));
        }

        // Sort
        items = _sortColumn switch
        {
            "Name" => _sortAscending ? items.OrderBy(p => p.Name) : items.OrderByDescending(p => p.Name),
            "Pid" => _sortAscending ? items.OrderBy(p => p.Pid) : items.OrderByDescending(p => p.Pid),
            "RamBytes" => _sortAscending ? items.OrderBy(p => p.RamBytes) : items.OrderByDescending(p => p.RamBytes),
            "DiskBytesPerSec" => _sortAscending ? items.OrderBy(p => p.DiskBytesPerSec) : items.OrderByDescending(p => p.DiskBytesPerSec),
            _ => _sortAscending ? items.OrderBy(p => p.CpuPercent) : items.OrderByDescending(p => p.CpuPercent)
        };

        var sortedList = items.ToList();
        FilteredCount = sortedList.Count;

        // Synchronize FilteredProcesses without recreating collection (preserves UI virtualization)
        SyncObservableCollection(FilteredProcesses, sortedList);
    }

    private static bool MatchesFuzzy(ProcessItemViewModel item, string query)
    {
        // 1. Direct PID match
        if (item.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. High-performance subsequence fuzzy matcher (with gap handling)
        return FuzzyMatcher.IsMatch(item.Name, query);
    }

    private static void SyncObservableCollection<T>(ObservableCollection<T> target, IList<T> source)
    {
        // If counts differ significantly, reset to avoid O(N^2) moves
        if (Math.Abs(target.Count - source.Count) > 20)
        {
            target.Clear();
            foreach (var item in source)
            {
                target.Add(item);
            }
            return;
        }

        // In-place align
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

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
