using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NeuralTask.Core.Models;

namespace NeuralTask.App.ViewModels;

public class ProcessListViewModel : ViewModelBase
{
    private readonly Dictionary<int, ProcessItemViewModel> _allProcessesMap = new();
    private readonly List<ProcessItemViewModel> _allProcessesList = new();

    private string _searchText = string.Empty;
    private string _sortColumn = "CpuPercent";
    private bool _sortAscending = false;
    private ProcessItemViewModel? _selectedProcess;
    private int _totalCount;
    private int _filteredCount;

    public ObservableCollection<ProcessItemViewModel> FilteredProcesses { get; } = new();

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

    private void ApplyFilterAndSort()
    {
        IEnumerable<ProcessItemViewModel> items = _allProcessesList;

        // Fuzzy filter
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

        // 2. Substring match
        if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Sub-sequence fuzzy match (e.g. "chr" matches "chrome.exe")
        int queryIdx = 0;
        for (int i = 0; i < item.Name.Length && queryIdx < query.Length; i++)
        {
            if (char.ToLowerInvariant(item.Name[i]) == char.ToLowerInvariant(query[queryIdx]))
            {
                queryIdx++;
            }
        }

        return queryIdx == query.Length;
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
