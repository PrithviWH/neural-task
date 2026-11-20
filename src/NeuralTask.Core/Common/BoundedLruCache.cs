using System;
using System.Collections.Generic;

namespace NeuralTask.Core.Common;

/// <summary>
/// Thread-safe, bounded Least-Recently-Used (LRU) cache.
/// Prevents unbounded memory growth in long-running sessions by automatically evicting
/// least-recently-accessed entries once maximum capacity is reached.
/// </summary>
/// <typeparam name="TKey">Cache key type.</typeparam>
/// <typeparam name="TValue">Cache value type.</typeparam>
public class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxCapacity;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly object _lock = new();

    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _map;
    private readonly LinkedList<CacheItem> _lruList = new();

    private sealed record CacheItem(TKey Key, TValue Value);

    /// <summary>
    /// Gets the maximum configured capacity of the cache.
    /// </summary>
    public int MaxCapacity => _maxCapacity;

    /// <summary>
    /// Gets the current number of cached entries.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="BoundedLruCache{TKey, TValue}"/>.
    /// </summary>
    /// <param name="maxCapacity">Maximum number of entries before LRU eviction occurs.</param>
    /// <param name="comparer">Optional key equality comparer (defaults to EqualityComparer.Default).</param>
    public BoundedLruCache(int maxCapacity = 500, IEqualityComparer<TKey>? comparer = null)
    {
        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCapacity), "Maximum capacity must be greater than zero.");
        }

        _maxCapacity = maxCapacity;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _map = new Dictionary<TKey, LinkedListNode<CacheItem>>(_maxCapacity, _comparer);
    }

    /// <summary>
    /// Attempts to retrieve a value from the cache. If found, promotes the entry to Most Recently Used (MRU).
    /// </summary>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Promote to MRU (head of list)
                if (node != _lruList.First)
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                }

                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// Inserts or updates an entry in the cache, promoting it to Most Recently Used.
    /// If capacity is exceeded, evicts the Least Recently Used (tail) entry.
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existingNode))
            {
                // Update existing node and promote to MRU
                _lruList.Remove(existingNode);
                var updatedNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
                _lruList.AddFirst(updatedNode);
                _map[key] = updatedNode;
                return;
            }

            // Evict LRU entry if at capacity
            if (_map.Count >= _maxCapacity && _lruList.Last != null)
            {
                var lruNode = _lruList.Last;
                _lruList.RemoveLast();
                _map.Remove(lruNode.Value.Key);
            }

            var newNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
            _lruList.AddFirst(newNode);
            _map[key] = newNode;
        }
    }

    /// <summary>
    /// Retrieves an existing entry or computes and stores it via the provided factory.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (valueFactory == null) throw new ArgumentNullException(nameof(valueFactory));

        if (TryGetValue(key, out var existing) && existing != null)
        {
            return existing;
        }

        var created = valueFactory(key);
        Set(key, created);
        return created;
    }

    /// <summary>
    /// Removes an entry by key if it exists in the cache.
    /// </summary>
    public bool Remove(TKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _map.Remove(key);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Trims the cache down to the specified target capacity, evicting the least recently used entries.
    /// </summary>
    public void TrimTo(int targetCapacity)
    {
        if (targetCapacity < 0) throw new ArgumentOutOfRangeException(nameof(targetCapacity));

        lock (_lock)
        {
            while (_map.Count > targetCapacity && _lruList.Last != null)
            {
                var lru = _lruList.Last;
                _lruList.RemoveLast();
                _map.Remove(lru.Value.Key);
            }
        }
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lruList.Clear();
        }
    }

    /// <summary>
    /// Returns the cached keys ordered from Most Recently Used to Least Recently Used.
    /// </summary>
    public IReadOnlyList<TKey> GetKeysInMruOrder()
    {
        lock (_lock)
        {
            var keys = new List<TKey>(_lruList.Count);
            for (var node = _lruList.First; node != null; node = node.Next)
            {
                keys.Add(node.Value.Key);
            }
            return keys;
        }
    }
}
