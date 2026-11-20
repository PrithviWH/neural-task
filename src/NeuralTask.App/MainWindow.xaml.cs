using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using NeuralTask.App.Services;
using NeuralTask.App.ViewModels;
using NeuralTask.App.Views;
using NeuralTask.Core.Models;
using NeuralTask.Theming;
using Wpf.Ui.Controls;
using Brushes = System.Windows.Media.Brushes;

namespace NeuralTask.App;

public partial class MainWindow : FluentWindow
{
    private int _currentTabIndex = 0;
    private bool _isExplicitExit = false;
    private readonly IAppShutdownCoordinator _shutdownCoordinator;
    private readonly List<(SafePluginPanel Panel, Border NavBorder, System.Windows.Shapes.Path Icon, System.Windows.Controls.TextBlock Label)> _pluginNavItems = new();
    private SafePluginPanel? _activePluginPanel;
    private readonly Action<SafePluginPanel> _pluginPanelLoadedHandler;
    private readonly Action<SafePluginPanel> _pluginPanelFaultedHandler;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel? viewModel = null, IAppShutdownCoordinator? shutdownCoordinator = null)
    {
        InitializeComponent();

        ViewModel = viewModel ?? AppHost.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = ViewModel;

        var historyStore = AppHost.Services.GetService<NeuralTask.Core.Services.IHistoryStore>();
        var settingsService = AppHost.Services.GetRequiredService<ISettingsService>();

        _shutdownCoordinator = shutdownCoordinator ?? new AppShutdownCoordinator(
            settingsService,
            historyStore,
            ViewModel,
            historyLoadTaskProvider: () => App.HistoryLoadTask,
            closeAction: () =>
            {
                _isExplicitExit = true;
                Dispatcher.Invoke(() =>
                {
                    Close();
                    Application.Current?.Shutdown(0);
                });
            });

        ViewDashboard.DataContext = ViewModel.Dashboard;
        ViewProcesses.DataContext = ViewModel.ProcessList;
        ViewStartup.DataContext = ViewModel.Startup;
        ViewSettings.DataContext = ViewModel.Settings;

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
                return;
            }

            if (_shutdownCoordinator.ShutdownState == 0)
            {
                e.Cancel = true;
                _ = _shutdownCoordinator.ExecuteShutdownAsync();
            }
            else if (_shutdownCoordinator.ShutdownState == 1)
            {
                e.Cancel = true;
            }
            // If ShutdownState == 2, allow the window to close normally
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

        _pluginPanelLoadedHandler = _ => Dispatcher.BeginInvoke(BuildPluginNavigationRail);
        _pluginPanelFaultedHandler = _ => Dispatcher.BeginInvoke(BuildPluginNavigationRail);

        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            ViewModel.RequestRestoreWindow -= RestoreAndShowWindow;
            ViewModel.RequestExitApplication -= ExitApplication;
            ViewModel.PluginManager.PanelLoaded -= _pluginPanelLoadedHandler;
            ViewModel.PluginManager.PanelFaulted -= _pluginPanelFaultedHandler;
        };

        SelectTab(0);
        BuildPluginNavigationRail();
        ViewModel.PluginManager.PanelLoaded += _pluginPanelLoadedHandler;
        ViewModel.PluginManager.PanelFaulted += _pluginPanelFaultedHandler;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.D1 || e.Key == Key.D)
            {
                SelectTab(0);
                e.Handled = true;
            }
            else if (e.Key == Key.D2 || e.Key == Key.P)
            {
                SelectTab(1);
                e.Handled = true;
            }
            else if (e.Key == Key.D3 || e.Key == Key.S)
            {
                SelectTab(2);
                e.Handled = true;
            }
            else if (e.Key == Key.D4 || e.Key == Key.O)
            {
                SelectTab(3);
                e.Handled = true;
            }
        }
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

    public Task ExecuteAsyncShutdownAsync()
    {
        return _shutdownCoordinator.ExecuteShutdownAsync();
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        if (_shutdownCoordinator.ShutdownState == 0)
        {
            _ = ExecuteAsyncShutdownAsync();
        }
        else if (_shutdownCoordinator.ShutdownState == 2)
        {
            Close();
            Application.Current?.Shutdown(0);
        }
    }

    private void OnThemeChanged(object? sender, ThemeMode theme)
    {
        // Re-apply navigation highlighting to pick up refreshed theme dynamic brushes
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

        var activeBg = (Brush)FindResource("BrushAccentPrimarySoft");
        var activeText = (Brush)FindResource("BrushAccentPrimary");
        var inactiveText = (Brush)FindResource("BrushTextSecondary");

        // Reset plugin indicators
        foreach (var item in _pluginNavItems)
        {
            item.NavBorder.Background = Brushes.Transparent;
            item.Icon.Stroke = inactiveText;
            item.Label.Foreground = inactiveText;
        }

        // Apply Clarity tab highlighting
        BorderDashboard.Background = index == 0 ? activeBg : Brushes.Transparent;
        IconDashboard.Stroke = index == 0 ? activeText : inactiveText;
        LabelDashboard.Foreground = index == 0 ? activeText : inactiveText;

        BorderProcesses.Background = index == 1 ? activeBg : Brushes.Transparent;
        IconProcesses.Stroke = index == 1 ? activeText : inactiveText;
        LabelProcesses.Foreground = index == 1 ? activeText : inactiveText;

        BorderStartup.Background = index == 2 ? activeBg : Brushes.Transparent;
        IconStartup.Stroke = index == 2 ? activeText : inactiveText;
        LabelStartup.Foreground = index == 2 ? activeText : inactiveText;

        BorderSettings.Background = index == 3 ? activeBg : Brushes.Transparent;
        IconSettings.Stroke = index == 3 ? activeText : inactiveText;
        LabelSettings.Foreground = index == 3 ? activeText : inactiveText;

        // Switch View with smooth fade-in
        SwitchView(index);
    }

    public void SelectPluginTab(SafePluginPanel panel)
    {
        _activePluginPanel = panel;
        _currentTabIndex = -1;

        var activeBg = (Brush)FindResource("BrushAccentPrimarySoft");
        var activeText = (Brush)FindResource("BrushAccentPrimary");
        var inactiveText = (Brush)FindResource("BrushTextSecondary");

        // Reset built-in tabs
        BorderDashboard.Background = Brushes.Transparent;
        IconDashboard.Stroke = inactiveText;
        LabelDashboard.Foreground = inactiveText;

        BorderProcesses.Background = Brushes.Transparent;
        IconProcesses.Stroke = inactiveText;
        LabelProcesses.Foreground = inactiveText;

        BorderStartup.Background = Brushes.Transparent;
        IconStartup.Stroke = inactiveText;
        LabelStartup.Foreground = inactiveText;

        BorderSettings.Background = Brushes.Transparent;
        IconSettings.Stroke = inactiveText;
        LabelSettings.Foreground = inactiveText;

        // Highlight selected plugin tab
        foreach (var item in _pluginNavItems)
        {
            bool isSelected = item.Panel == panel;
            item.NavBorder.Background = isSelected ? activeBg : Brushes.Transparent;
            item.Icon.Stroke = isSelected ? activeText : inactiveText;
            item.Label.Foreground = isSelected ? activeText : inactiveText;
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
                From = 0.4,
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

        var navButtonStyle = (Style)FindResource("ClarityNavButtonStyle");
        var inactiveText = (Brush)FindResource("BrushTextSecondary");

        foreach (var panel in ViewModel.PluginManager.LoadedPanels)
        {
            if (!panel.IsEnabled) continue;

            var border = new Border
            {
                Width = 72,
                Height = 58,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 3, 0, 3),
                Background = Brushes.Transparent
            };

            var btn = new Button
            {
                Style = navButtonStyle,
                ToolTip = $"{panel.Title} Plugin"
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, panel.Title);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconPath = new System.Windows.Shapes.Path
            {
                Stroke = inactiveText,
                StrokeThickness = 1.75,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            try
            {
                iconPath.Data = Geometry.Parse(panel.IconGeometry);
            }
            catch
            {
                iconPath.Data = (Geometry)FindResource("IconGpuGeometry");
            }

            string shortTitle = panel.Title.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? "GPU" : panel.Title;
            var label = new System.Windows.Controls.TextBlock
            {
                Text = shortTitle,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                FontFamily = (FontFamily)FindResource("FontFamilyUI"),
                Foreground = inactiveText,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(iconPath);
            stack.Children.Add(label);
            btn.Content = stack;

            var targetPanel = panel;
            btn.Click += (_, _) => SelectPluginTab(targetPanel);

            border.Child = btn;
            NavRailPluginsContainer.Children.Add(border);
            _pluginNavItems.Add((panel, border, iconPath, label));
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
            From = 0.4,
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
        ViewDashboard.ScrollToTop();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_dark.png"));

        // 1b. Dark Theme - Dashboard Lower Section (Storage, Network, Intelligence)
        ViewDashboard.ScrollToBottom();
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_intelligence_dark.png"));
        ViewDashboard.ScrollToTop();

        // 1c. Dark Theme - Dashboard (Scrubbing historical mode)
        ViewModel.Dashboard.ScrubberValue = 0.0;
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_scrubbing_dark.png"));
        ViewModel.Dashboard.ResumeLive();
        await Task.Delay(200);

        // 2. Dark Theme - Processes (Default with Inspector Sidecar)
        SelectTab(1);
        ViewModel.ProcessList.TriggerReScan();
        if (ViewModel.ProcessList.FilteredProcesses.Count > 0 && ViewModel.ProcessList.SelectedProcess == null)
        {
            ViewModel.ProcessList.SelectedProcess = ViewModel.ProcessList.FilteredProcesses[0];
        }
        await Task.Delay(750);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_dark.png"));
        SaveBitmap(Path.Combine(outputDir, "screen_processes_inspector_dark.png"));

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

        // 5b. Dark Theme - GPU Plugin Panel
        var gpuPluginDark = ViewModel.PluginManager.LoadedPanels.FirstOrDefault(p => p.Id == "gpu-monitor");
        if (gpuPluginDark != null)
        {
            SelectPluginTab(gpuPluginDark);
            await Task.Delay(400);
            SaveBitmap(Path.Combine(outputDir, "screen_gpu_dark.png"));

            if (PluginViewportPresenter.Content is DependencyObject depObj)
            {
                var sv = FindVisualChild<ScrollViewer>(depObj);
                if (sv != null)
                {
                    sv.ScrollToBottom();
                    await Task.Delay(300);
                    SaveBitmap(Path.Combine(outputDir, "screen_gpu_processes_dark.png"));
                    sv.ScrollToTop();
                }
            }
        }

        // 6. Light Theme - Dashboard
        ThemeManager.ApplyTheme(ThemeMode.Light);
        SelectTab(0);
        ViewDashboard.ScrollToTop();
        await Task.Delay(400);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_light.png"));

        // 6b. Light Theme - Dashboard Lower Section (Storage, Network, Intelligence)
        ViewDashboard.ScrollToBottom();
        await Task.Delay(300);
        SaveBitmap(Path.Combine(outputDir, "screen_dashboard_intelligence_light.png"));
        ViewDashboard.ScrollToTop();

        // 7. Light Theme - Processes (Default with Inspector Sidecar)
        SelectTab(1);
        ViewModel.ProcessList.TriggerReScan();
        if (ViewModel.ProcessList.FilteredProcesses.Count > 0 && ViewModel.ProcessList.SelectedProcess == null)
        {
            ViewModel.ProcessList.SelectedProcess = ViewModel.ProcessList.FilteredProcesses[0];
        }
        await Task.Delay(750);
        SaveBitmap(Path.Combine(outputDir, "screen_processes_light.png"));
        SaveBitmap(Path.Combine(outputDir, "screen_processes_inspector_light.png"));

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

        // 10b. Light Theme - GPU Plugin Panel
        var gpuPluginLight = ViewModel.PluginManager.LoadedPanels.FirstOrDefault(p => p.Id == "gpu-monitor");
        if (gpuPluginLight != null)
        {
            SelectPluginTab(gpuPluginLight);
            await Task.Delay(400);
            SaveBitmap(Path.Combine(outputDir, "screen_gpu_light.png"));

            if (PluginViewportPresenter.Content is DependencyObject depObjLight)
            {
                var sv = FindVisualChild<ScrollViewer>(depObjLight);
                if (sv != null)
                {
                    sv.ScrollToBottom();
                    await Task.Delay(300);
                    SaveBitmap(Path.Combine(outputDir, "screen_gpu_processes_light.png"));
                    sv.ScrollToTop();
                }
            }
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}
