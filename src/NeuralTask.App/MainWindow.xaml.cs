using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using NeuralTask.App.Services;
using NeuralTask.App.ViewModels;
using NeuralTask.App.Views;
using NeuralTask.Core.Models;
using NeuralTask.Theming;
using Wpf.Ui.Controls;

namespace NeuralTask.App;

public partial class MainWindow : FluentWindow
{
    private int _currentTabIndex = 0;
    private Storyboard? _ambientStoryboard;
    private bool _isExplicitExit = false;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel? viewModel = null)
    {
        InitializeComponent();

        ViewModel = viewModel ?? AppHost.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = ViewModel;

        ViewDashboard.DataContext = ViewModel.Dashboard;
        ViewProcesses.DataContext = ViewModel.ProcessList;
        ViewSettings.DataContext = ViewModel.Settings;

        SetupAmbientAnimation();
        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;

        ViewModel.RequestRestoreWindow += RestoreAndShowWindow;
        ViewModel.RequestExitApplication += ExitApplication;

        StateChanged += (s, e) =>
        {
            if (WindowState == WindowState.Minimized && ViewModel.Settings.IsMinimizeToTrayEnabled)
            {
                Hide();
            }
        };

        Closing += (s, e) =>
        {
            if (!_isExplicitExit && ViewModel.Settings.IsMinimizeToTrayEnabled)
            {
                e.Cancel = true;
                Hide();
            }
        };

        Loaded += async (_, _) =>
        {
            WindowState = WindowState.Normal;
            Topmost = true;
            Activate();
            Focus();
            try
            {
                await ViewModel.StartMonitoringAsync();
            }
            catch
            {
            }
        };

        Closed += async (_, _) =>
        {
            try
            {
                await ViewModel.StopMonitoringAsync();
            }
            catch
            {
            }
        };

        SelectTab(0);
    }

    public void RestoreAndShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
        Topmost = true;
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
        Application.Current.Shutdown();
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsAmbientAnimationEnabled))
        {
            var isEnabled = ViewModel.Settings.IsAmbientAnimationEnabled;
            AmbientAnimationCanvas.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (isEnabled)
            {
                _ambientStoryboard?.Resume();
            }
            else
            {
                _ambientStoryboard?.Pause();
            }
        }
    }

    private void SetupAmbientAnimation()
    {
        var animX = new DoubleAnimation
        {
            From = -100,
            To = 100,
            Duration = TimeSpan.FromSeconds(25),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        var animY = new DoubleAnimation
        {
            From = -50,
            To = 50,
            Duration = TimeSpan.FromSeconds(18),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        _ambientStoryboard = new Storyboard();
        _ambientStoryboard.Children.Add(animX);
        _ambientStoryboard.Children.Add(animY);

        Storyboard.SetTarget(animX, AmbientTransform);
        Storyboard.SetTargetProperty(animX, new PropertyPath(TranslateTransform.XProperty));

        Storyboard.SetTarget(animY, AmbientTransform);
        Storyboard.SetTargetProperty(animY, new PropertyPath(TranslateTransform.YProperty));

        _ambientStoryboard.Begin();
    }

    private void OnThemeChanged(object? sender, ThemeMode theme)
    {
        // Re-apply nav button highlighting to pick up refreshed theme dynamic brushes
        SelectTab(_currentTabIndex);
    }

    private void OnNavDashboardClick(object sender, RoutedEventArgs e) => SelectTab(0);
    private void OnNavProcessesClick(object sender, RoutedEventArgs e) => SelectTab(1);
    private void OnNavSettingsClick(object sender, RoutedEventArgs e) => SelectTab(2);

    public void SelectTab(int index)
    {
        _currentTabIndex = index;
        ViewModel.SelectedTabIndex = index;

        // Reset all indicators
        IndicatorDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        IndicatorProcesses.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        IndicatorSettings.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        // Apply theme-driven accent stroke & glow to active icon
        var activeBrush = (Brush)FindResource("BrushAccentPrimary");
        var inactiveBrush = (Brush)FindResource("BrushTextSecondary");
        var activeGlowColor = (Color)FindResource("ColorAccentPrimary");

        var activeGlow = new DropShadowEffect
        {
            BlurRadius = 8,
            ShadowDepth = 0,
            Color = activeGlowColor,
            Opacity = 0.8
        };

        IconDashboard.Stroke = index == 0 ? activeBrush : inactiveBrush;
        IconDashboard.Effect = index == 0 ? activeGlow : null;

        IconProcesses.Stroke = index == 1 ? activeBrush : inactiveBrush;
        IconProcesses.Effect = index == 1 ? activeGlow : null;

        IconSettings.Stroke = index == 2 ? activeBrush : inactiveBrush;
        IconSettings.Effect = index == 2 ? activeGlow : null;

        // Switch View with smooth fade-in
        SwitchView(index);
    }

    private void SwitchView(int index)
    {
        UIElement targetView = index switch
        {
            0 => ViewDashboard,
            1 => ViewProcesses,
            2 => ViewSettings,
            _ => ViewDashboard
        };

        ViewDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewProcesses.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        var fadeIn = new DoubleAnimation
        {
            From = 0.3,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        targetView.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public async Task CaptureScreenshotsAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        // Wait for live monitoring to collect initial real metrics
        await Task.Delay(1200);

        // 1. Dark Theme - Dashboard (Live metrics)
        ThemeManager.ApplyTheme(ThemeMode.Dark);
        SelectTab(0);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_dark.png"));

        // 2. Dark Theme - Processes (Live processes)
        SelectTab(1);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_dark.png"));

        // 3. Dark Theme - Settings
        SelectTab(2);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_dark.png"));

        // 4. Light Theme - Dashboard
        ThemeManager.ApplyTheme(ThemeMode.Light);
        SelectTab(0);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_light.png"));

        // 5. Light Theme - Processes
        SelectTab(1);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_light.png"));

        // 6. Light Theme - Settings
        SelectTab(2);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_light.png"));

        // 7. Kill-Tree Dialog Neural Topology Screenshot
        var dummyRoot = new ProcessSnapshot(12480, "parent_service.exe", 0, 4.2, 250_000_000, 1024, 0, DateTime.Now, null, false);
        var dummyChildren = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(12481, "worker_node_1.exe", 12480, 2.1, 120_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12482, "worker_node_2.exe", 12480, 1.8, 115_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12483, "telemetry_agent.exe", 12480, 0.4, 45_000_000, 128, 0, DateTime.Now, null, false)
        };
        var killDialog = new KillTreeDialog(dummyRoot, dummyChildren) { Width = 680, Height = 540 };
        killDialog.Show();
        await Task.Delay(300);
        SaveElementBitmap(killDialog, Path.Combine(outputDir, "screen_kill_tree_dialog.png"));
        killDialog.Close();

        // Revert to Dark
        ThemeManager.ApplyTheme(ThemeMode.Dark);
        SelectTab(0);
    }

    private void SaveElementBitmap(FrameworkElement element, string filePath)
    {
        element.UpdateLayout();
        var width = (int)Math.Max(element.ActualWidth, 680);
        var height = (int)Math.Max(element.ActualHeight, 540);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    private void SaveBitmap(string filePath)
    {
        UpdateLayout();
        var width = (int)Math.Max(ActualWidth, 1100);
        var height = (int)Math.Max(ActualHeight, 720);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
