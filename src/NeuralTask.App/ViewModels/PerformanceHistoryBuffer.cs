using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace NeuralTask.App.ViewModels;

/// <summary>
/// Thread-safe bounded ring buffer holding 60 seconds of telemetry points.
/// Computes normalized WPF PointCollections and smoothed Bezier PathGeometries
/// for modern gradient area and line chart rendering (PRD Clarity Spec §6).
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

    public PerformanceHistoryBuffer(int maxSamples = 64)
    {
        _maxSamples = Math.Max(10, maxSamples);
    }

    /// <summary>
    /// Pre-seeds the history buffer with an initial baseline value across all sample slots,
    /// ensuring the timeline immediately spans the full viewport width from edge to edge.
    /// </summary>
    public void Seed(double value, int count = 0)
    {
        lock (_lock)
        {
            _samples.Clear();
            int n = count > 0 ? count : _maxSamples;
            double safeVal = Math.Max(0.0, value);
            for (int i = 0; i < n; i++)
            {
                _samples.Enqueue(safeVal);
            }
        }
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

    public double GetPeak()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 0.0;
            return _samples.Max();
        }
    }

    public double GetLatest()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 0.0;
            return _samples.Last();
        }
    }

    public double GetAverage()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 0.0;
            return _samples.Average();
        }
    }

    public double GetMinimum()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 0.0;
            return _samples.Min();
        }
    }

    /// <summary>
    /// Calculates the normalized Y coordinate within viewport height for a given metric value.
    /// </summary>
    public static double CalculateY(double val, double height, double maxScale = 100.0)
    {
        double safeScale = maxScale > 0.001 ? maxScale : 100.0;
        double clamped = Math.Clamp(val, 0.0, safeScale);
        double y = height - ((clamped / safeScale) * (height - 14.0)) - 7.0;
        return Math.Round(Math.Clamp(y, 4.0, height - 2.0), 1);
    }

    /// <summary>
    /// Generates a normalized WPF PointCollection scaled to the target rendering viewport.
    /// Preserved for legacy polyline bindings and backward compatibility.
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

    /// <summary>
    /// Generates a smoothed cubic Bezier curve geometry for the telemetry line.
    /// </summary>
    public PathGeometry GenerateSmoothCurveGeometry(double width, double height, double maxScale = 100.0)
    {
        var pts = GetScaledPoints(width, height, maxScale);
        return BuildCurveGeometry(pts, width, height);
    }

    /// <summary>
    /// Generates a closed area geometry under the smoothed cubic Bezier curve for gradient fills.
    /// </summary>
    public PathGeometry GenerateSmoothAreaGeometry(double width, double height, double maxScale = 100.0)
    {
        var pts = GetScaledPoints(width, height, maxScale);
        return BuildAreaGeometry(pts, width, height);
    }

    public static PathGeometry GenerateSmoothCurveFromList(IReadOnlyList<double> items, int maxSamples, double width, double height, double maxScale = 100.0)
    {
        var pts = ScalePoints(items, maxSamples, width, height, maxScale);
        return BuildCurveGeometry(pts, width, height);
    }

    public static PathGeometry GenerateSmoothAreaFromList(IReadOnlyList<double> items, int maxSamples, double width, double height, double maxScale = 100.0)
    {
        var pts = ScalePoints(items, maxSamples, width, height, maxScale);
        return BuildAreaGeometry(pts, width, height);
    }

    private static PathGeometry BuildCurveGeometry(Point[] pts, double width, double height)
    {
        var geometry = new PathGeometry();
        if (pts.Length == 0) return geometry;

        var figure = new PathFigure { StartPoint = pts[0], IsClosed = false };
        if (pts.Length == 1)
        {
            figure.Segments.Add(new LineSegment(new Point(width, pts[0].Y), true));
        }
        else
        {
            AddSmoothSegments(figure, pts, height);
        }

        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static PathGeometry BuildAreaGeometry(Point[] pts, double width, double height)
    {
        var geometry = new PathGeometry();
        if (pts.Length == 0) return geometry;

        var figure = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        if (pts.Length == 1)
        {
            figure.Segments.Add(new LineSegment(new Point(width, pts[0].Y), true));
            figure.Segments.Add(new LineSegment(new Point(width, height), false));
            figure.Segments.Add(new LineSegment(new Point(0, height), false));
        }
        else
        {
            AddSmoothSegments(figure, pts, height);
            figure.Segments.Add(new LineSegment(new Point(pts[^1].X, height), false));
            figure.Segments.Add(new LineSegment(new Point(pts[0].X, height), false));
        }

        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private Point[] GetScaledPoints(double width, double height, double maxScale)
    {
        double[] items;
        lock (_lock)
        {
            if (_samples.Count == 0) return Array.Empty<Point>();
            items = _samples.ToArray();
        }

        return ScalePoints(items, 60, width, height, maxScale);
    }

    private static Point[] ScalePoints(IReadOnlyList<double> items, int visibleSamples, double width, double height, double maxScale)
    {
        if (items.Count == 0) return Array.Empty<Point>();

        const int leadCount = 4;
        int safeVisible = Math.Max(10, visibleSamples);
        double stepX = width / Math.Max(1, safeVisible - 1);

        if (items.Count >= safeVisible + leadCount)
        {
            // Fully buffered with delay (e.g. 60 visible + 4 lead = 64 samples):
            // Right edge (x = width) is at sample index (items.Count - 1 - leadCount),
            // which represents telemetry recorded 4 seconds ago.
            // The remaining leadCount real samples extend beyond x = width up to (width + leadCount * stepX).
            // As the chart container translates leftward by fraction * stepX at 60 FPS,
            // the curve enters seamlessly from the right edge with 100% genuine data continuity.
            int sliceCount = safeVisible + leadCount;
            var pts = new Point[sliceCount];
            int startIdx = items.Count - leadCount - safeVisible;

            for (int i = 0; i < safeVisible; i++)
            {
                double x = i * stepX;
                double val = Math.Clamp(items[startIdx + i], 0.0, maxScale);
                pts[i] = new Point(Math.Round(x, 1), CalculateY(val, height, maxScale));
            }

            for (int k = 1; k <= leadCount; k++)
            {
                double x = width + (k * stepX);
                double val = Math.Clamp(items[startIdx + safeVisible - 1 + k], 0.0, maxScale);
                pts[safeVisible - 1 + k] = new Point(Math.Round(x, 1), CalculateY(val, height, maxScale));
            }

            return pts;
        }
        else if (items.Count >= safeVisible)
        {
            // Buffer has at least visibleSamples but fewer than visibleSamples + leadCount
            int actualLead = Math.Min(leadCount, items.Count - safeVisible);
            int startIdx = items.Count - actualLead - safeVisible;
            int sliceCount = safeVisible + leadCount;
            var pts = new Point[sliceCount];

            for (int i = 0; i < safeVisible; i++)
            {
                double x = i * stepX;
                double val = Math.Clamp(items[startIdx + i], 0.0, maxScale);
                pts[i] = new Point(Math.Round(x, 1), CalculateY(val, height, maxScale));
            }

            for (int k = 1; k <= actualLead; k++)
            {
                double x = width + (k * stepX);
                double val = Math.Clamp(items[startIdx + safeVisible - 1 + k], 0.0, maxScale);
                pts[safeVisible - 1 + k] = new Point(Math.Round(x, 1), CalculateY(val, height, maxScale));
            }

            double lastVal = items[^1];
            for (int k = actualLead + 1; k <= leadCount; k++)
            {
                double x = width + (k * stepX);
                pts[safeVisible - 1 + k] = new Point(Math.Round(x, 1), CalculateY(lastVal, height, maxScale));
            }

            return pts;
        }
        else
        {
            // Initial warm-up or small test cases:
            // Distribute available points up to width, and extend lead points past width.
            int sliceCount = items.Count + leadCount;
            var pts = new Point[sliceCount];
            double startX = width - ((items.Count - 1) * stepX);

            for (int i = 0; i < items.Count; i++)
            {
                double x = startX + (i * stepX);
                double val = Math.Clamp(items[i], 0.0, maxScale);
                pts[i] = new Point(Math.Round(x, 1), CalculateY(val, height, maxScale));
            }

            double lastVal = items[^1];
            for (int k = 1; k <= leadCount; k++)
            {
                double x = width + (k * stepX);
                pts[items.Count - 1 + k] = new Point(Math.Round(x, 1), CalculateY(lastVal, height, maxScale));
            }

            return pts;
        }
    }

    private static void AddSmoothSegments(PathFigure figure, Point[] pts, double height)
    {
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];

            // Monotone-like cubic Bezier control points
            double cp1X = p1.X + (p2.X - p0.X) / 6.0;
            double cp1Y = Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6.0, 2.0, height);

            double cp2X = p2.X - (p3.X - p1.X) / 6.0;
            double cp2Y = Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6.0, 2.0, height);

            figure.Segments.Add(new BezierSegment(
                new Point(Math.Round(cp1X, 1), Math.Round(cp1Y, 1)),
                new Point(Math.Round(cp2X, 1), Math.Round(cp2Y, 1)),
                p2,
                true));
        }
    }
}
