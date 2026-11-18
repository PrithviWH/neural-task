using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeuralTask.Contracts;

namespace NeuralTask.App.Services;

/// <summary>
/// Fault-isolated host wrapper for external <see cref="INeuralTaskPanel"/> instances.
/// Intercepts all lifecycle and telemetry exceptions, preventing broken plugins
/// from crashing the host application.
/// </summary>
public class SafePluginPanel : IDisposable
{
    private readonly INeuralTaskPanel _inner;
    private Grid? _containerGrid;
    private FrameworkElement? _userView;
    private Border? _faultView;

    public string Id => _inner.Id;
    public string Title => _inner.Title;
    public string Description => _inner.Description;
    public string IconGeometry => _inner.IconGeometry;
    public int Order => _inner.Order;
    public string? AssemblyPath { get; }

    public bool IsEnabled { get; set; } = true;
    public bool IsFaulted { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public event Action<SafePluginPanel>? FaultOccurred;
    public event Action<SafePluginPanel>? StateChanged;

    public SafePluginPanel(INeuralTaskPanel inner, string? assemblyPath = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        AssemblyPath = assemblyPath;
    }

    /// <summary>
    /// Safely invokes Initialize() on the underlying plugin.
    /// </summary>
    public bool InitializeSafe()
    {
        if (IsFaulted) return false;

        try
        {
            _inner.Initialize();
            return true;
        }
        catch (Exception ex)
        {
            RecordFault("Initialize", ex);
            return false;
        }
    }

    /// <summary>
    /// Returns the UI view container for this panel. If the plugin has faulted,
    /// a failure HUD banner is displayed in place of the plugin view.
    /// </summary>
    public FrameworkElement GetOrCreateView()
    {
        _containerGrid ??= new Grid();
        _containerGrid.Children.Clear();

        if (IsFaulted)
        {
            _faultView = BuildFaultView();
            _containerGrid.Children.Add(_faultView);
            return _containerGrid;
        }

        try
        {
            _userView ??= _inner.CreateView();
            _containerGrid.Children.Add(_userView);
        }
        catch (Exception ex)
        {
            RecordFault("CreateView", ex);
            _faultView = BuildFaultView();
            _containerGrid.Children.Add(_faultView);
        }

        return _containerGrid;
    }

    /// <summary>
    /// Safely dispatches a telemetry tick to the underlying plugin.
    /// </summary>
    public void OnTelemetryTickSafe(PluginTelemetryContext? context)
    {
        if (IsFaulted || !IsEnabled) return;

        try
        {
            _inner.OnTelemetryTick(context);
        }
        catch (Exception ex)
        {
            RecordFault("OnTelemetryTick", ex);

            // Switch UI container to fault view on the dispatcher thread if needed
            if (_containerGrid != null)
            {
                if (_containerGrid.Dispatcher.CheckAccess())
                {
                    SwitchToFaultView();
                }
                else
                {
                    _containerGrid.Dispatcher.BeginInvoke(SwitchToFaultView);
                }
            }
        }
    }

    /// <summary>
    /// Attempts to recover a faulted panel by clearing the fault state and re-initializing.
    /// </summary>
    public bool Retry()
    {
        IsFaulted = false;
        LastErrorMessage = null;
        _userView = null;

        var initSuccess = InitializeSafe();
        if (initSuccess)
        {
            GetOrCreateView();
            StateChanged?.Invoke(this);
            return true;
        }

        return false;
    }

    private void RecordFault(string phase, Exception ex)
    {
        IsFaulted = true;
        LastErrorMessage = $"[{phase}] {ex.GetType().Name}: {ex.Message}";
        FaultOccurred?.Invoke(this);
        StateChanged?.Invoke(this);
    }

    private void SwitchToFaultView()
    {
        if (_containerGrid == null) return;
        _containerGrid.Children.Clear();
        _faultView = BuildFaultView();
        _containerGrid.Children.Add(_faultView);
    }

    private Border BuildFaultView()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 18, 18, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 82, 82)), // Radiant red/amber
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(24),
            Padding = new Thickness(24),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 640
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };

        // Header Pip & Title
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var pip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = $"PLUGIN FAULT // {Title.ToUpperInvariant()}",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
            FontFamily = new FontFamily("Consolas, Courier New, Segoe UI")
        };
        headerPanel.Children.Add(pip);
        headerPanel.Children.Add(titleText);
        stack.Children.Add(headerPanel);

        // Subtitle / Identifier
        var idText = new TextBlock
        {
            Text = $"ID: {Id}   |   SOURCE: {System.IO.Path.GetFileName(AssemblyPath ?? "memory")}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),
            Margin = new Thickness(0, 0, 0, 16),
            FontFamily = new FontFamily("Consolas, Courier New, Segoe UI")
        };
        stack.Children.Add(idText);

        // Explanation
        var descText = new TextBlock
        {
            Text = "The plugin encountered an unhandled exception during execution and has been isolated to protect the host monitoring subsystem.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        stack.Children.Add(descText);

        // Error Detail Box
        var errorBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 10, 10, 14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 82, 82)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 20)
        };
        var errorText = new TextBlock
        {
            Text = LastErrorMessage ?? "Unknown exception.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 170)),
            FontFamily = new FontFamily("Consolas, Courier New, Segoe UI"),
            TextWrapping = TextWrapping.Wrap
        };
        errorBox.Child = errorText;
        stack.Children.Add(errorBox);

        // Action Buttons Row
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var retryBtn = new Button
        {
            Content = "[ 🔄 RETRY INITIALIZATION ]",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 229, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        retryBtn.Click += (_, _) => Retry();

        var disableBtn = new Button
        {
            Content = "[ 🚫 DISABLE PANEL ]",
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 82, 82)),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        disableBtn.Click += (_, _) =>
        {
            IsEnabled = false;
            StateChanged?.Invoke(this);
        };

        buttonPanel.Children.Add(retryBtn);
        buttonPanel.Children.Add(disableBtn);
        stack.Children.Add(buttonPanel);

        border.Child = stack;
        return border;
    }

    public void ShutdownSafe()
    {
        try
        {
            _inner.Shutdown();
        }
        catch (Exception ex)
        {
            RecordFault("Shutdown", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            _inner.Dispose();
        }
        catch
        {
        }
    }
}
