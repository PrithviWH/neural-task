using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NeuralTask.App.Services;
using NeuralTask.Contracts;
using Xunit;

namespace NeuralTask.Core.Tests;

public class SafePluginPanelTests
{
    private static void RunInSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }
    }
    private class DummyGoodPanel : INeuralTaskPanel
    {
        public string Id => "dummy-good";
        public string Title => "Good Panel";
        public string Description => "A well-behaved test panel";
        public string IconGeometry => "M 0,0 L 10,10";
        public int Order => 50;

        public bool Initialized { get; private set; }
        public bool ViewCreated { get; private set; }
        public int TickCount { get; private set; }
        public bool ShutdownCalled { get; private set; }
        public bool Disposed { get; private set; }

        public void Initialize() => Initialized = true;
        public FrameworkElement CreateView()
        {
            ViewCreated = true;
            return new TextBlock { Text = "Good View" };
        }
        public void OnTelemetryTick(PluginTelemetryContext? context) => TickCount++;
        public void Shutdown() => ShutdownCalled = true;
        public void Dispose() => Disposed = true;
    }

    private class CrashingPanel : INeuralTaskPanel
    {
        private readonly bool _crashInit;
        private readonly bool _crashView;
        private readonly bool _crashTick;

        public string Id => "dummy-crash";
        public string Title => "Crashing Panel";
        public string Description => "A misbehaving test panel";
        public string IconGeometry => "M 0,0 L 10,10";
        public int Order => 99;

        public CrashingPanel(bool crashInit = false, bool crashView = false, bool crashTick = false)
        {
            _crashInit = crashInit;
            _crashView = crashView;
            _crashTick = crashTick;
        }

        public void Initialize()
        {
            if (_crashInit) throw new InvalidOperationException("Fatal init failure from plugin!");
        }

        public FrameworkElement CreateView()
        {
            if (_crashView) throw new NullReferenceException("Plugin UI creation crashed!");
            return new TextBlock { Text = "Crashing View" };
        }

        public void OnTelemetryTick(PluginTelemetryContext? context)
        {
            if (_crashTick) throw new DivideByZeroException("Plugin math fault!");
        }

        public void Shutdown() { }
        public void Dispose() { }
    }

    [Fact]
    public void SafePluginPanel_WithWellBehavedPlugin_ExecutesLifecycleCleanly()
    {
        RunInSta(() =>
        {
            var dummy = new DummyGoodPanel();
            using var safe = new SafePluginPanel(dummy);

            Assert.Equal("dummy-good", safe.Id);
            Assert.Equal("Good Panel", safe.Title);
            Assert.False(safe.IsFaulted);
            Assert.True(safe.IsEnabled);

            Assert.True(safe.InitializeSafe());
            Assert.True(dummy.Initialized);

            var view = safe.GetOrCreateView();
            Assert.NotNull(view);
            Assert.True(dummy.ViewCreated);

            var context = new PluginTelemetryContext(15.5, 16_000_000_000, 8_000_000_000, 8_000_000_000, 150, DateTime.UtcNow);
            safe.OnTelemetryTickSafe(context);
            Assert.Equal(1, dummy.TickCount);
            Assert.False(safe.IsFaulted);

            safe.ShutdownSafe();
            Assert.True(dummy.ShutdownCalled);
        });
    }

    [Fact]
    public void SafePluginPanel_WhenInitializeThrows_InterceptsExceptionAndMarksFaulted()
    {
        RunInSta(() =>
        {
            var crashing = new CrashingPanel(crashInit: true);
            using var safe = new SafePluginPanel(crashing);

            bool faultFired = false;
            safe.FaultOccurred += _ => faultFired = true;

            bool success = safe.InitializeSafe();
            Assert.False(success);
            Assert.True(safe.IsFaulted);
            Assert.True(faultFired);
            Assert.Contains("InvalidOperationException", safe.LastErrorMessage);
            Assert.Contains("Fatal init failure", safe.LastErrorMessage);
        });
    }

    [Fact]
    public void SafePluginPanel_WhenCreateViewThrows_ReturnsFaultBannerWithoutThrowing()
    {
        RunInSta(() =>
        {
            var crashing = new CrashingPanel(crashView: true);
            using var safe = new SafePluginPanel(crashing);

            safe.InitializeSafe();
            var view = safe.GetOrCreateView();

            Assert.NotNull(view);
            Assert.True(safe.IsFaulted);
            Assert.Contains("NullReferenceException", safe.LastErrorMessage);
        });
    }

    [Fact]
    public void SafePluginPanel_WhenTelemetryTickThrows_InterceptsExceptionAndMarksFaulted()
    {
        RunInSta(() =>
        {
            var crashing = new CrashingPanel(crashTick: true);
            using var safe = new SafePluginPanel(crashing);

            safe.InitializeSafe();
            Assert.False(safe.IsFaulted);

            var context = new PluginTelemetryContext(10.0, 1000, 500, 500, 10, DateTime.UtcNow);
            // Must not throw to caller
            safe.OnTelemetryTickSafe(context);

            Assert.True(safe.IsFaulted);
            Assert.Contains("DivideByZeroException", safe.LastErrorMessage);
        });
    }

    [Fact]
    public void SafePluginPanel_WhenDisabled_SkipsTelemetryTicks()
    {
        RunInSta(() =>
        {
            var dummy = new DummyGoodPanel();
            using var safe = new SafePluginPanel(dummy);

            safe.InitializeSafe();
            safe.IsEnabled = false;

            var context = new PluginTelemetryContext(10.0, 1000, 500, 500, 10, DateTime.UtcNow);
            safe.OnTelemetryTickSafe(context);

            Assert.Equal(0, dummy.TickCount);
        });
    }
}
