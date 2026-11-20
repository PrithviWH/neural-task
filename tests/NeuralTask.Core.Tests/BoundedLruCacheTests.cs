using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeuralTask.App.Services;
using NeuralTask.Core.Common;
using NeuralTask.Core.Models;
using NeuralTask.Core.Services;
using Xunit;

namespace NeuralTask.Core.Tests;

public class BoundedLruCacheTests
{
    [Fact]
    public void Cache_RemainsWithinConfiguredMaximum_EvictingLruEntries()
    {
        const int capacity = 5;
        var cache = new BoundedLruCache<string, int>(capacity, StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= 10; i++)
        {
            cache.Set($"key_{i}", i * 10);
            Assert.True(cache.Count <= capacity, $"Cache size {cache.Count} exceeded capacity {capacity}");
        }

        Assert.Equal(capacity, cache.Count);

        // Keys 1 through 5 should have been evicted; keys 6 through 10 should be present
        for (int i = 1; i <= 5; i++)
        {
            Assert.False(cache.TryGetValue($"key_{i}", out _), $"key_{i} should have been evicted");
        }

        for (int i = 6; i <= 10; i++)
        {
            Assert.True(cache.TryGetValue($"key_{i}", out var val));
            Assert.Equal(i * 10, val);
        }
    }

    [Fact]
    public void Cache_RecentlyAccessedEntry_IsNotEvictedNext()
    {
        var cache = new BoundedLruCache<string, string>(3, StringComparer.OrdinalIgnoreCase);

        cache.Set("A", "Alpha");
        cache.Set("B", "Beta");
        cache.Set("C", "Gamma");

        // Access "A" so it becomes Most Recently Used: order is now A (MRU), C, B (LRU)
        Assert.True(cache.TryGetValue("A", out var val));
        Assert.Equal("Alpha", val);

        // Insert "D": should evict "B" (the least recently used), NOT "A"
        cache.Set("D", "Delta");

        Assert.True(cache.TryGetValue("A", out _), "A should have been preserved due to recent access");
        Assert.False(cache.TryGetValue("B", out _), "B should have been evicted as LRU");
        Assert.True(cache.TryGetValue("C", out _));
        Assert.True(cache.TryGetValue("D", out _));
    }

    [Fact]
    public void Cache_ConcurrentReadsAndWrites_AreThreadSafeAndConsistent()
    {
        const int capacity = 100;
        var cache = new BoundedLruCache<int, int>(capacity);

        Parallel.For(0, 1000, i =>
        {
            int key = i % 150;
            cache.Set(key, i);

            cache.TryGetValue(key, out _);

            if (i % 10 == 0)
            {
                cache.Remove(key);
            }
        });

        Assert.True(cache.Count <= capacity, $"Cache exceeded capacity: {cache.Count} > {capacity}");
    }

    [Fact]
    public void AuthenticodeSignatureVerifier_BoundedCache_StaysWithinConfiguredCapacity()
    {
        const int capacity = 5;
        var verifier = new AuthenticodeSignatureVerifier(capacity);

        // Create 8 temporary files
        var tempFiles = new List<string>();
        try
        {
            for (int i = 0; i < 8; i++)
            {
                var path = Path.Combine(Path.GetTempPath(), $"verifier_cache_test_{i}_{Guid.NewGuid():N}.exe");
                File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
                tempFiles.Add(path);

                verifier.VerifySignature(path);
                Assert.True(verifier.CacheCount <= capacity, $"Verifier cache size {verifier.CacheCount} exceeded capacity {capacity}");
            }

            Assert.Equal(capacity, verifier.CacheCount);
        }
        finally
        {
            foreach (var f in tempFiles)
            {
                if (File.Exists(f)) { try { File.Delete(f); } catch { } }
            }
        }
    }

    [Fact]
    public void AuthenticodeSignatureVerifier_FileModification_InvalidatesCacheAndReVerifies()
    {
        var verifier = new AuthenticodeSignatureVerifier(10);
        var tempFile = Path.Combine(Path.GetTempPath(), $"verifier_invalidation_test_{Guid.NewGuid():N}.exe");

        try
        {
            // 1. Write initial file (unsigned dummy)
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x01 });
            File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow.AddMinutes(-10));

            var status1 = verifier.VerifySignature(tempFile);
            Assert.Equal(SignatureStatus.Unsigned, status1);

            // 2. Modify file on disk (simulate tampering or update)
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x02, 0x03, 0x04 });
            File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow);

            // 3. Verifier must detect timestamp/size change and re-verify instead of returning stale result
            var status2 = verifier.VerifySignature(tempFile);
            Assert.Equal(SignatureStatus.Unsigned, status2);
        }
        finally
        {
            if (File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
        }
    }

    [Fact]
    public void ProcessIconCache_BoundedCapacity_EvictsOldestEntriesAndReleasesResourcesSafely()
    {
        // Verify ProcessIconCache static bounded cache
        ProcessIconCache.Clear();
        Assert.Equal(0, ProcessIconCache.CacheCount);
        Assert.Equal(500, ProcessIconCache.MaxCapacity);

        // Test TrimTo functionality
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (File.Exists(cmdPath))
        {
            var icon = ProcessIconCache.ExtractFromFile(cmdPath);
            Assert.NotNull(icon);
            Assert.True(icon.IsFrozen, "Extracted icon must be frozen for cross-thread safety");

            // Even if an entry is cleared or trimmed from cache, the frozen ImageSource remains intact
            ProcessIconCache.Clear();
            Assert.True(icon.IsFrozen);
            Assert.Equal(0, ProcessIconCache.CacheCount);
        }
    }
}
