using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeuralTask.Core.Common;

namespace NeuralTask.App.Services;

/// <summary>
/// High-performance thread-safe bounded icon cache and extractor for Windows executables.
/// Extracts native 32x32 / 16x16 application icons and converts them to frozen WPF BitmapSources
/// to prevent UI thread blocking and GDI handle exhaustion.
/// Bounded to 500 entries with thread-safe LRU eviction to prevent unbounded memory growth.
/// </summary>
public static class ProcessIconCache
{
    public const int DefaultCacheCapacity = 500;

    public sealed record IconCacheEntry(ImageSource? Icon, DateTime LastWriteTimeUtc);

    private static readonly BoundedLruCache<string, IconCacheEntry> Cache =
        new(DefaultCacheCapacity, StringComparer.OrdinalIgnoreCase);

    public static int CacheCount => Cache.Count;
    public static int MaxCapacity => Cache.MaxCapacity;

    public static void Clear() => Cache.Clear();
    public static void TrimTo(int targetCapacity) => Cache.TrimTo(targetCapacity);

    /// <summary>
    /// Synchronously retrieves the cached icon, or queues background extraction if not yet cached.
    /// Invalidates and re-extracts if the file timestamp has changed on disk.
    /// </summary>
    public static ImageSource? GetIcon(string? executablePath, Action<ImageSource?>? onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        DateTime currentLastWrite = DateTime.MinValue;
        try
        {
            if (File.Exists(executablePath))
            {
                currentLastWrite = File.GetLastWriteTimeUtc(executablePath);
            }
        }
        catch
        {
        }

        if (Cache.TryGetValue(executablePath, out var cached) && cached != null)
        {
            // If the file modification time matches (or is inaccessible), return cached icon
            if (currentLastWrite == DateTime.MinValue || cached.LastWriteTimeUtc == currentLastWrite)
            {
                return cached.Icon;
            }
        }

        // Fire asynchronous background extraction to prevent UI hitching
        _ = Task.Run(() =>
        {
            try
            {
                var extracted = ExtractFromFile(executablePath);
                Cache.Set(executablePath, new IconCacheEntry(extracted, currentLastWrite));
                if (extracted != null && onLoaded != null)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            onLoaded(extracted);
                        }
                        catch
                        {
                        }
                    });
                }
            }
            catch
            {
            }
        });

        return null;
    }

    /// <summary>
    /// Extracts the native icon from an executable file and freezes the resulting BitmapSource.
    /// </summary>
    public static ImageSource? ExtractFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        try
        {
            if (!File.Exists(filePath)) return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            var hIcon = icon.Handle;
            if (hIcon == IntPtr.Zero) return null;

            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Freezing enables cross-thread WPF rendering and high performance
            bitmapSource.Freeze();

            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }
}
