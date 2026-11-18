using System;
using System.Diagnostics;

namespace NeuralTask.Core.Services;

/// <summary>
/// Time abstraction interface enabling deterministic delta testing in unit tests.
/// </summary>
public interface ITimeSource
{
    DateTimeOffset UtcNow { get; }
    long TimestampTicks { get; }
    double TicksPerSecond { get; }
}

public class SystemTimeSource : ITimeSource
{
    public static readonly SystemTimeSource Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long TimestampTicks => Stopwatch.GetTimestamp();

    public double TicksPerSecond => Stopwatch.Frequency;
}
