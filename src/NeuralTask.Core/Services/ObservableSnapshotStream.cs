using System;
using System.Collections.Generic;
using NeuralTask.Core.Models;

namespace NeuralTask.Core.Services;

/// <summary>
/// Lightweight, thread-safe implementation of IObservable for process snapshots.
/// Eliminates dependency on external reactive libraries in the core layer.
/// </summary>
public sealed class ObservableSnapshotStream : IObservable<IReadOnlyList<ProcessSnapshot>>, IDisposable
{
    private readonly List<IObserver<IReadOnlyList<ProcessSnapshot>>> _observers = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    public IDisposable Subscribe(IObserver<IReadOnlyList<ProcessSnapshot>> observer)
    {
        if (observer == null) throw new ArgumentNullException(nameof(observer));

        lock (_lock)
        {
            if (_isDisposed)
            {
                observer.OnCompleted();
                return EmptyDisposable.Instance;
            }

            _observers.Add(observer);
            return new Subscription(this, observer);
        }
    }

    public void Publish(IReadOnlyList<ProcessSnapshot> snapshot)
    {
        List<IObserver<IReadOnlyList<ProcessSnapshot>>> observersCopy;

        lock (_lock)
        {
            if (_isDisposed) return;
            observersCopy = new List<IObserver<IReadOnlyList<ProcessSnapshot>>>(_observers);
        }

        foreach (var observer in observersCopy)
        {
            try
            {
                observer.OnNext(snapshot);
            }
            catch
            {
                // Isolate observer exceptions from pipeline
            }
        }
    }

    public void Dispose()
    {
        List<IObserver<IReadOnlyList<ProcessSnapshot>>> observersCopy;

        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            observersCopy = new List<IObserver<IReadOnlyList<ProcessSnapshot>>>(_observers);
            _observers.Clear();
        }

        foreach (var observer in observersCopy)
        {
            try
            {
                observer.OnCompleted();
            }
            catch
            {
            }
        }
    }

    private void Unsubscribe(IObserver<IReadOnlyList<ProcessSnapshot>> observer)
    {
        lock (_lock)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ObservableSnapshotStream _stream;
        private readonly IObserver<IReadOnlyList<ProcessSnapshot>> _observer;
        private bool _disposed;

        public Subscription(ObservableSnapshotStream stream, IObserver<IReadOnlyList<ProcessSnapshot>> observer)
        {
            _stream = stream;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Unsubscribe(_observer);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
