using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeuralTask.App.ViewModels;

namespace NeuralTask.App.Views;

public partial class DashboardView : UserControl
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastTickTimestamp;
    private double _sampleIntervalMs = 1000.0;
    private DashboardViewModel? _subscribedVm;

    public DashboardView()
    {
        InitializeComponent();
        _lastTickTimestamp = _stopwatch.ElapsedMilliseconds;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        CompositionTarget.Rendering += OnCompositionTargetRendering;

        if (ChartCardGrid0 != null && ChartCardGrid0.ActualWidth > 50 && DataContext is DashboardViewModel vm)
        {
            vm.CardViewportWidth = ChartCardGrid0.ActualWidth;
            vm.ChartViewportWidth = ChartCardGrid0.ActualWidth;
        }

        HookViewModel(DataContext as DashboardViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        UnhookViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnhookViewModel();
        HookViewModel(e.NewValue as DashboardViewModel);
    }

    private void HookViewModel(DashboardViewModel? vm)
    {
        if (vm == null) return;
        _subscribedVm = vm;
        _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnhookViewModel()
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.PrimaryCurveGeometry))
        {
            long now = _stopwatch.ElapsedMilliseconds;
            double elapsedSinceLast = now - _lastTickTimestamp;
            if (elapsedSinceLast > 300 && elapsedSinceLast < 2500)
            {
                // Smooth moving average of sample arrival rate to absorb timer jitter
                _sampleIntervalMs = (_sampleIntervalMs * 0.85) + (elapsedSinceLast * 0.15);
                // Advance phase reference by the interval so fraction resets smoothly without jumping
                _lastTickTimestamp += (long)_sampleIntervalMs;
                if (Math.Abs(now - _lastTickTimestamp) > _sampleIntervalMs)
                {
                    _lastTickTimestamp = now;
                }
            }
            else
            {
                _lastTickTimestamp = now;
            }
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;

        double width = vm.CardViewportWidth;
        double stepX = width / Math.Max(1, 59);

        long now = _stopwatch.ElapsedMilliseconds;
        double elapsed = Math.Max(0, now - _lastTickTimestamp);
        // Continuous 60 FPS phase translation without hard clamping
        double fraction = elapsed / Math.Max(100.0, _sampleIntervalMs);
        double shiftX = -fraction * stepX;

        if (ChartTranslateTransform0 != null) ChartTranslateTransform0.X = shiftX;
        if (ChartTranslateTransform1 != null) ChartTranslateTransform1.X = shiftX;
        if (ChartTranslateTransform2 != null) ChartTranslateTransform2.X = shiftX;
        if (ChartTranslateTransform3 != null) ChartTranslateTransform3.X = shiftX;
        if (ChartTranslateTransform4 != null) ChartTranslateTransform4.X = shiftX;
        if (ChartTranslateTransform5 != null) ChartTranslateTransform5.X = shiftX;
    }

    private void ChartCardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 50 && DataContext is DashboardViewModel vm)
        {
            vm.CardViewportWidth = e.NewSize.Width;
            vm.ChartViewportWidth = e.NewSize.Width;
        }
    }

    public void ScrollToTop() => DashboardScrollViewer.ScrollToTop();
    public void ScrollToBottom() => DashboardScrollViewer.ScrollToBottom();
}
