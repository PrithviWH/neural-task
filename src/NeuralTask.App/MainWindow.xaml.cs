using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    private readonly List<(SafePluginPanel Panel, Grid NavGrid, System.Windows.Shapes.Rectangle Indicator, System.Windows.Shapes.Path Icon)> _pluginNavItems = new();
    private SafePluginPanel? _activePluginPanel;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel? viewModel = null)
    {
        InitializeComponent();

        ViewModel = viewModel ?? AppHost.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = ViewModel;

        ViewDashboard.DataContext = ViewModel.Dashboard;
        ViewProcesses.DataContext = ViewModel.ProcessList;
        ViewStartup.DataContext = ViewModel.Startup;
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
            Activate();
            Focus();

            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 9); // SW_RESTORE
                SetForegroundWindow(handle);
            }

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
        BuildPluginNavigationRail();
        ViewModel.PluginManager.PanelLoaded += _ => Dispatcher.BeginInvoke(BuildPluginNavigationRail);
        ViewModel.PluginManager.PanelFaulted += _ => Dispatcher.BeginInvoke(BuildPluginNavigationRail);
    }

    public void RestoreAndShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, 9); // SW_RESTORE
            SetForegroundWindow(handle);
        }
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
        if (_activePluginPanel != null)
        {
            SelectPluginTab(_activePluginPanel);
        }
        else
        {
            SelectTab(_currentTabIndex);
        }
    }

    private void OnNavDashboardClick(object sender, RoutedEventArgs e) => SelectTab(0);
    private void OnNavProcessesClick(object sender, RoutedEventArgs e) => SelectTab(1);
    private void OnNavStartupClick(object sender, RoutedEventArgs e) => SelectTab(2);
    private void OnNavSettingsClick(object sender, RoutedEventArgs e) => SelectTab(3);

    public void SelectTab(int index)
    {
        _currentTabIndex = index;
        ViewModel.SelectedTabIndex = index;
        _activePluginPanel = null;

        if (PluginViewportPresenter != null)
        {
            PluginViewportPresenter.Visibility = Visibility.Collapsed;
            PluginViewportPresenter.Content = null;
        }

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

        // Reset plugin indicators
        foreach (var item in _pluginNavItems)
        {
            item.Indicator.Visibility = Visibility.Collapsed;
            item.Icon.Stroke = inactiveBrush;
            item.Icon.Effect = null;
        }

        // Reset all indicators
        IndicatorDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        IndicatorProcesses.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        IndicatorStartup.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        IndicatorSettings.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        IconDashboard.Stroke = index == 0 ? activeBrush : inactiveBrush;
        IconDashboard.Effect = index == 0 ? activeGlow : null;

        IconProcesses.Stroke = index == 1 ? activeBrush : inactiveBrush;
        IconProcesses.Effect = index == 1 ? activeGlow : null;

        IconStartup.Stroke = index == 2 ? activeBrush : inactiveBrush;
        IconStartup.Effect = index == 2 ? activeGlow : null;

        IconSettings.Stroke = index == 3 ? activeBrush : inactiveBrush;
        IconSettings.Effect = index == 3 ? activeGlow : null;

        // Switch View with smooth fade-in
        SwitchView(index);
    }

    public void SelectPluginTab(SafePluginPanel panel)
    {
        _activePluginPanel = panel;
        _currentTabIndex = -1;

        // Reset built-in indicators and icons
        IndicatorDashboard.Visibility = Visibility.Collapsed;
        IndicatorProcesses.Visibility = Visibility.Collapsed;
        IndicatorStartup.Visibility = Visibility.Collapsed;
        IndicatorSettings.Visibility = Visibility.Collapsed;

        var activeBrush = (Brush)FindResource("BrushAccentPrimary");
        var inactiveBrush = (Brush)FindResource("BrushTextSecondary");
        var activeGlowColor = (Color)FindResource("ColorAccentPrimary");
        var activeGlow = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Color = activeGlowColor, Opacity = 0.8 };

        IconDashboard.Stroke = inactiveBrush; IconDashboard.Effect = null;
        IconProcesses.Stroke = inactiveBrush; IconProcesses.Effect = null;
        IconStartup.Stroke = inactiveBrush; IconStartup.Effect = null;
        IconSettings.Stroke = inactiveBrush; IconSettings.Effect = null;

        // Highlight selected plugin tab
        foreach (var item in _pluginNavItems)
        {
            bool isSelected = item.Panel == panel;
            item.Indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            item.Icon.Stroke = isSelected ? activeBrush : inactiveBrush;
            item.Icon.Effect = isSelected ? activeGlow : null;
        }

        // Hide built-in views and show plugin view
        ViewDashboard.Visibility = Visibility.Collapsed;
        ViewProcesses.Visibility = Visibility.Collapsed;
        ViewStartup.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;

        if (PluginViewportPresenter != null)
        {
            PluginViewportPresenter.Content = panel.GetOrCreateView();
            PluginViewportPresenter.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation
            {
                From = 0.3,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            PluginViewportPresenter.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }

    public void BuildPluginNavigationRail()
    {
        if (NavRailPluginsContainer == null) return;

        NavRailPluginsContainer.Children.Clear();
        _pluginNavItems.Clear();

        var navButtonStyle = (Style)FindResource("NavRailButtonStyle");
        var inactiveBrush = (Brush)FindResource("BrushTextSecondary");
        var activeGlowColor = (Color)FindResource("ColorAccentPrimary");

        foreach (var panel in ViewModel.PluginManager.LoadedPanels)
        {
            if (!panel.IsEnabled) continue;

            var grid = new Grid();

            var indicator = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 3,
                Height = 28,
                Fill = (Brush)FindResource("BrushAccentPrimary"),
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(-8, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Color = activeGlowColor,
                    Opacity = 0.8
                }
            };

            var btn = new Button
            {
                Style = navButtonStyle,
                ToolTip = $"{panel.Title} (PRD §8.5 Plugin)"
            };

            var iconPath = new System.Windows.Shapes.Path
            {
                Stroke = inactiveBrush,
                StrokeThickness = 1.5,
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform
            };

            try
            {
                iconPath.Data = Geometry.Parse(panel.IconGeometry);
            }
            catch
            {
                iconPath.Data = Geometry.Parse("M 4,4 L 20,4 L 20,20 L 4,20 Z");
            }

            btn.Content = iconPath;

            var targetPanel = panel;
            btn.Click += (_, _) => SelectPluginTab(targetPanel);

            grid.Children.Add(indicator);
            grid.Children.Add(btn);

            NavRailPluginsContainer.Children.Add(grid);
            _pluginNavItems.Add((panel, grid, indicator, iconPath));
        }
    }

    private void SwitchView(int index)
    {
        UIElement targetView = index switch
        {
            0 => ViewDashboard,
            1 => ViewProcesses,
            2 => ViewStartup,
            3 => ViewSettings,
            _ => ViewDashboard
        };

        ViewDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewProcesses.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewStartup.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

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

        // 1. Dark Theme - Dashboard (Live metrics with scrubber & export)
        ThemeManager.ApplyTheme(ThemeMode.Dark);
        SelectTab(0);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_dark.png"));

        // 1b. Dark Theme - Dashboard (Scrubbing historical mode)
        ViewModel.Dashboard.ScrubberValue = 0.0;
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_scrubbing_dark.png"));
        ViewModel.Dashboard.ResumeLive();
        await Task.Delay(200);

        // 2. Dark Theme - Processes (Live processes & Heuristic Flags)
        SelectTab(1);
        if (ViewModel.ProcessList.FilteredProcesses.Count > 0)
        {
            ViewModel.ProcessList.SelectedProcess = ViewModel.ProcessList.FilteredProcesses[0];
        }
        ViewModel.ProcessList.TriggerReScan();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_dark.png"));

        // 3. Dark Theme - Startup (PRD §8.3 Matrix)
        SelectTab(2);
        await ViewModel.Startup.LoadEntriesAsync();
        if (ViewModel.Startup.FilteredEntries.Count > 0)
        {
            ViewModel.Startup.FilteredEntries[0].IsExpanded = true;
        }
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_startup_dark.png"));

        // 4. Dark Theme - Settings
        SelectTab(3);
        ViewSettings.ScrollToTop();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_dark.png"));

        // 4b. Dark Theme - Settings Plugins Section
        ViewSettings.ScrollToBottom();
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_plugins_dark.png"));
        ViewSettings.ScrollToTop();

        // 5. Dark Theme - Kill-Tree Dialog Neural Topology Screenshot
        var dummyRootDark = new ProcessSnapshot(12480, "parent_service.exe", 0, 4.2, 250_000_000, 1024, 0, DateTime.Now, null, false);
        var dummyChildrenDark = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(12481, "worker_node_1.exe", 12480, 2.1, 120_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12482, "worker_node_2.exe", 12480, 1.8, 115_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12483, "telemetry_agent.exe", 12480, 0.4, 45_000_000, 128, 0, DateTime.Now, null, false)
        };
        var killDialogDark = new KillTreeDialog(dummyRootDark, dummyChildrenDark) { Width = 680, Height = 540 };
        killDialogDark.Show();
        await Task.Delay(300);
        SaveElementBitmap(killDialogDark, Path.Combine(outputDir, "screen_kill_tree_dialog.png"));
        SaveElementBitmap(killDialogDark, Path.Combine(outputDir, "screen_kill_tree_dialog_dark.png"));
        killDialogDark.Close();

        // 5b. Dark Theme - GPU Plugin Panel (PRD §8.5)
        var gpuPluginDark = ViewModel.PluginManager.LoadedPanels.FirstOrDefault(p => p.Id == "gpu-monitor");
        if (gpuPluginDark != null)
        {
            SelectPluginTab(gpuPluginDark);
            await Task.Delay(400);
            SaveBitmap(Path.Combine(outputDir, "screen_gpu_dark.png"));
        }

        // 6. Light Theme - Dashboard
        ThemeManager.ApplyTheme(ThemeMode.Light);
        SelectTab(0);
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_light.png"));

        // 7. Light Theme - Processes
        SelectTab(1);
        if (ViewModel.ProcessList.FilteredProcesses.Count > 0)
        {
            ViewModel.ProcessList.SelectedProcess = ViewModel.ProcessList.FilteredProcesses[0];
        }
        ViewModel.ProcessList.TriggerReScan();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_light.png"));

        // 8. Light Theme - Startup
        SelectTab(2);
        if (ViewModel.Startup.FilteredEntries.Count > 0)
        {
            ViewModel.Startup.FilteredEntries[0].IsExpanded = true;
        }
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_startup_light.png"));

        // 9. Light Theme - Settings
        SelectTab(3);
        ViewSettings.ScrollToTop();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_light.png"));

        // 9b. Light Theme - Settings Plugins Section
        ViewSettings.ScrollToBottom();
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_settings_plugins_light.png"));
        ViewSettings.ScrollToTop();

        // 10. Light Theme - Kill-Tree Dialog
        var dummyRootLight = new ProcessSnapshot(12480, "parent_service.exe", 0, 4.2, 250_000_000, 1024, 0, DateTime.Now, null, false);
        var dummyChildrenLight = new List<ProcessSnapshot>
        {
            new ProcessSnapshot(12481, "worker_node_1.exe", 12480, 2.1, 120_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12482, "worker_node_2.exe", 12480, 1.8, 115_000_000, 512, 0, DateTime.Now, null, false),
            new ProcessSnapshot(12483, "telemetry_agent.exe", 12480, 0.4, 45_000_000, 128, 0, DateTime.Now, null, false)
        };
        var killDialogLight = new KillTreeDialog(dummyRootLight, dummyChildrenLight) { Width = 680, Height = 540 };
        killDialogLight.Show();
        await Task.Delay(300);
        SaveElementBitmap(killDialogLight, Path.Combine(outputDir, "screen_kill_tree_dialog_light.png"));
        killDialogLight.Close();

        // 10b. Light Theme - GPU Plugin Panel (PRD §8.5)
        var gpuPluginLight = ViewModel.PluginManager.LoadedPanels.FirstOrDefault(p => p.Id == "gpu-monitor");
        if (gpuPluginLight != null)
        {
            SelectPluginTab(gpuPluginLight);
            await Task.Delay(400);
            SaveBitmap(Path.Combine(outputDir, "screen_gpu_light.png"));
        }

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
