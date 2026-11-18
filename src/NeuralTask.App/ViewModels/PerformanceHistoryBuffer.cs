using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace NeuralTask.App.ViewModels;

/// <summary>
/// Thread-safe bounded ring buffer holding 60 seconds of telemetry points.
/// Computes normalized WPF PointCollections for smooth polyline chart rendering.
/// </summary>
public class PerformanceHistoryBuffer
{
    private readonly int _maxSamples;
    private readonly Queue<double> _samples = new();
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock) return _samples.Count;
        }
    }

    public PerformanceHistoryBuffer(int maxSamples = 60)
    {
        _maxSamples = Math.Max(10, maxSamples);
    }

    public void AddSample(double value)
    {
        lock (_lock)
        {
            if (_samples.Count >= _maxSamples)
            {
                _samples.Dequeue();
            }
            _samples.Enqueue(Math.Max(0.0, value));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
    }

    public IReadOnlyList<double> GetSnapshot()
    {
        lock (_lock)
        {
            return _samples.ToArray();
        }
    }

    /// <summary>
    /// Generates a normalized WPF PointCollection scaled to the target rendering viewport.
    /// </summary>
    public PointCollection GeneratePoints(double width, double height, double maxScale = 100.0)
    {
        var points = new PointCollection();
        double[] items;

        lock (_lock)
        {
            if (_samples.Count == 0)
            {
                points.Add(new Point(0, height));
                points.Add(new Point(width, height));
                return points;
            }
            items = _samples.ToArray();
        }

        if (items.Length == 1)
        {
            double y = height - (Math.Clamp(items[0], 0.0, maxScale) / maxScale * (height - 10.0));
            points.Add(new Point(0, y));
            points.Add(new Point(width, y));
            return points;
        }

        double stepX = width / Math.Max(1, _maxSamples - 1);
        double startX = width - ((items.Length - 1) * stepX);

        for (int i = 0; i < items.Length; i++)
        {
            double x = startX + (i * stepX);
            double val = Math.Clamp(items[i], 0.0, maxScale);
            double y = height - ((val / maxScale) * (height - 10.0));
            points.Add(new Point(Math.Round(x, 1), Math.Round(y, 1)));
        }

        return points;
    }
}
