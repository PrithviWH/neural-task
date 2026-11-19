using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeuralTask.App.Services;

/// <summary>
/// High-performance thread-safe icon cache and extractor for Windows executables.
/// Extracts native 32x32 / 16x16 application icons and converts them to frozen WPF BitmapSources
/// to prevent UI thread blocking and GDI handle exhaustion.
/// </summary>
public static class ProcessIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Synchronously retrieves the cached icon, or queues background extraction if not yet cached.
    /// </summary>
    public static ImageSource? GetIcon(string? executablePath, Action<ImageSource?>? onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (Cache.TryGetValue(executablePath, out var cached))
        {
            return cached;
        }

        // Fire asynchronous background extraction to prevent UI hitching
        _ = Task.Run(() =>
        {
            var extracted = ExtractFromFile(executablePath);
            Cache[executablePath] = extracted;
            if (extracted != null && onLoaded != null)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    onLoaded(extracted);
                });
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

            DestroyIcon(hIcon);
            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }
}
