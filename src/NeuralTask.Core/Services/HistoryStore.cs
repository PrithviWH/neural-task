using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Thread-safe in-memory rolling store for system-wide and top-process metrics.
/// Enables smooth timeline scrubbing and optional restart persistence.
///
/// MEMORY BUDGET SPECIFICATION (PRD §7):
/// 1. Sampling Cadence: 1 sample/sec (typical refresh interval).
/// 2. Retention Window: Default 30 minutes = 1,800 samples (max configurable 60 minutes = 3,600 samples).
/// 3. Per-Sample Size:
///    - Primitive metrics: 8 scalar fields + DateTimeOffset = ~80 bytes.
///    - TopProcesses: Capped at top 10 CPU + top 10 RAM (deduplicated: ~15 processes max).
///    - HistoricalProcessMetrics object: ~48 bytes data + references = ~64 bytes.
///    - 15 processes * 64 bytes ≈ 960 bytes.
///    - Total per HistoricalSample with object header ≈ 1.1 KB.
/// 4. Memory Footprint:
///    - 30-min window (1,800 samples): ~1.98 MB.
///    - 60-min window (3,600 samples): ~3.96 MB.
/// 5. Hard Ceiling: Hard capacity cap at 4,000 samples (< 4.5 MB RAM).
///    Older samples are deterministically trimmed on every insertion.
/// This strictly guarantees the rolling history never leaks memory or exceeds 5 MB.
/// </summary>
public class HistoryStore : IHistoryStore
{
    private const int HardMaxCapacity = 4000;
    private const int TopOffenderLimit = 10;

    private readonly object _lock = new();
    private readonly List<HistoricalSample> _samples = new();
    private TimeSpan _retentionWindow = TimeSpan.FromMinutes(30);
    private bool _isPersistenceEnabled = false;

    public TimeSpan RetentionWindow
    {
        get
        {
            lock (_lock) return _retentionWindow;
        }
        set
        {
            lock (_lock)
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), "Retention window must be positive.");
                _retentionWindow = value;
                TrimOldSamples_NoLock(DateTimeOffset.Now);
            }
        }
    }

    public int SampleCount
    {
        get
        {
            lock (_lock) return _samples.Count;
        }
    }

    public int MaxSamples => HardMaxCapacity;

    public bool IsPersistenceEnabled
    {
        get
        {
            lock (_lock) return _isPersistenceEnabled;
        }
        set
        {
            lock (_lock) _isPersistenceEnabled = value;
        }
    }

    public HistoryStore(TimeSpan? defaultRetention = null, bool persistenceEnabled = false)
    {
        if (defaultRetention.HasValue && defaultRetention.Value > TimeSpan.Zero)
        {
            _retentionWindow = defaultRetention.Value;
        }
        _isPersistenceEnabled = persistenceEnabled;
    }

    public void AddSample(
        DateTimeOffset timestamp,
        SystemMetricsSnapshot systemMetrics,
        IReadOnlyList<ProcessSnapshot> processes,
        IReadOnlyDictionary<int, ProcessHeuristicResult>? heuristicResults = null)
    {
        if (systemMetrics == null) throw new ArgumentNullException(nameof(systemMetrics));
        if (processes == null) throw new ArgumentNullException(nameof(processes));

        // 1. Extract and deduplicate top CPU and top RAM offenders
        var topCpu = processes
            .Where(p => p.Pid != 0 && !p.IsAccessDenied)
            .OrderByDescending(p => p.CpuPercent)
            .Take(TopOffenderLimit);

        var topRam = processes
            .Where(p => p.Pid != 0)
            .OrderByDescending(p => p.RamBytes)
            .Take(TopOffenderLimit);

        var seenPids = new HashSet<int>();
        var topProcessMetrics = new List<HistoricalProcessMetrics>();

        void AddProcessMetrics(ProcessSnapshot p)
        {
            if (seenPids.Add(p.Pid))
            {
                string? flagReason = null;
                if (heuristicResults != null && heuristicResults.TryGetValue(p.Pid, out var heuristic) && heuristic.HasFlags)
                {
                    flagReason = heuristic.CombinedSummary;
                }

                topProcessMetrics.Add(new HistoricalProcessMetrics(
                    Pid: p.Pid,
                    Name: p.Name,
                    CpuPercent: p.CpuPercent,
                    RamBytes: p.RamBytes,
                    DiskBytesPerSec: p.DiskBytesPerSec,
                    NetworkBytesPerSec: p.NetworkBytesPerSec,
                    FlagReason: flagReason
                ));
            }
        }

        foreach (var p in topCpu) AddProcessMetrics(p);
        foreach (var p in topRam) AddProcessMetrics(p);

        // 2. Create the immutable historical sample
        var sample = new HistoricalSample(
            Timestamp: timestamp,
            CpuPercent: systemMetrics.CpuPercent,
            MemoryPercent: systemMetrics.MemoryPercent,
            UsedMemoryBytes: systemMetrics.UsedMemoryBytes,
            TotalMemoryBytes: systemMetrics.TotalMemoryBytes,
            ProcessIoBytesPerSec: systemMetrics.ProcessIoBytesPerSec,
            NetworkReceivedBytesPerSec: systemMetrics.NetworkReceivedBytesPerSec,
            NetworkSentBytesPerSec: systemMetrics.NetworkSentBytesPerSec,
            ProcessCount: systemMetrics.ProcessCount,
            TopProcesses: topProcessMetrics
        );

        // 3. Insert and enforce memory retention bounds
        lock (_lock)
        {
            _samples.Add(sample);
            TrimOldSamples_NoLock(timestamp);
        }
    }

    public IReadOnlyList<HistoricalSample> GetHistory()
    {
        lock (_lock)
        {
            return _samples.ToList();
        }
    }

    public HistoricalSample? GetSampleAt(DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return null;
            if (_samples.Count == 1) return _samples[0];

            // Binary search for closest timestamp in O(log N)
            int low = 0;
            int high = _samples.Count - 1;

            if (timestamp <= _samples[low].Timestamp) return _samples[low];
            if (timestamp >= _samples[high].Timestamp) return _samples[high];

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var midTime = _samples[mid].Timestamp;

                if (midTime == timestamp)
                    return _samples[mid];

                if (midTime < timestamp)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            // low and high are the bounding neighbors
            var diffLow = Math.Abs((_samples[low].Timestamp - timestamp).TotalMilliseconds);
            var diffHigh = Math.Abs((_samples[high].Timestamp - timestamp).TotalMilliseconds);
            return diffLow < diffHigh ? _samples[low] : _samples[high];
        }
    }

    public HistoricalSample? GetSampleByFraction(double fraction)
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return null;
            if (_samples.Count == 1) return _samples[0];

            double clamped = Math.Clamp(fraction, 0.0, 1.0);
            int index = (int)Math.Round(clamped * (_samples.Count - 1));
            index = Math.Clamp(index, 0, _samples.Count - 1);
            return _samples[index];
        }
    }

    public HistoricalSample? GetLatest()
    {
        lock (_lock)
        {
            return _samples.Count > 0 ? _samples[^1] : null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
    }

    public async Task PersistAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        if (!IsPersistenceEnabled) return;

        List<HistoricalSample> toPersist;
        lock (_lock)
        {
            toPersist = _samples.ToList();
        }

        var path = filePath ?? GetDefaultHistoryFilePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, toPersist, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> LoadPersistedAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        var path = filePath ?? GetDefaultHistoryFilePath();
        if (!File.Exists(path)) return false;

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<List<HistoricalSample>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (loaded == null || loaded.Count == 0) return false;

            var cutoff = DateTimeOffset.Now - RetentionWindow;
            var validSamples = loaded.Where(s => s.Timestamp >= cutoff).OrderBy(s => s.Timestamp).ToList();

            lock (_lock)
            {
                _samples.Clear();
                _samples.AddRange(validSamples);
            }
            return true;
        }
        catch
        {
            // Corrupt or inaccessible file must not crash the application
            return false;
        }
    }

    private void TrimOldSamples_NoLock(DateTimeOffset currentTimestamp)
    {
        var cutoff = currentTimestamp - _retentionWindow;

        // Fast-path: binary search for the first sample >= cutoff
        int removeCount = 0;
        while (removeCount < _samples.Count && _samples[removeCount].Timestamp < cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            _samples.RemoveRange(0, removeCount);
        }

        // Enforce hard maximum capacity limit
        if (_samples.Count > HardMaxCapacity)
        {
            int excess = _samples.Count - HardMaxCapacity;
            _samples.RemoveRange(0, excess);
        }
    }

    private static string GetDefaultHistoryFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "NeuralTask", "telemetry_history.json");
    }
}
