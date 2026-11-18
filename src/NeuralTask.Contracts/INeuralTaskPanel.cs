using System;
using System.Windows;

namespace NeuralTask.Contracts;

/// <summary>
/// Minimal public contract for a NeuralTask extension panel.
/// Implement this interface in an external DLL to register a new view
/// in the shell navigation rail and participate in periodic telemetry ticks.
/// </summary>
public interface INeuralTaskPanel : IDisposable
{
    /// <summary>
    /// Unique lowercase identifier for the panel (e.g. "gpu-monitor", "network-radar").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable title displayed in navigation tooltips and panel headers.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Brief explanation of the panel's purpose and functionality.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Vector geometry string (WPF StreamGeometry / SVG Path syntax)
    /// rendered inside the shell navigation rail icon button.
    /// Example: "M 4,4 L 20,4 L 20,20 L 4,20 Z"
    /// </summary>
    string IconGeometry { get; }

    /// <summary>
    /// Relative sort order in the shell navigation rail.
    /// Lower values appear higher up in the rail. Default recommendation: 100+.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Invoked once by the host application when the plugin is loaded and verified.
    /// Perform one-time setup, background worker initialization, or dependency resolution here.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Instantiates and returns the root WPF FrameworkElement (such as a UserControl)
    /// to be hosted inside the main HUD viewport area.
    /// </summary>
    FrameworkElement CreateView();

    /// <summary>
    /// Periodic telemetry tick notification from the kernel monitoring loop (typically every 1 second).
    /// Used by the plugin to refresh gauges, charts, or internal counters.
    /// </summary>
    /// <param name="context">Snapshot of current system performance metrics.</param>
    void OnTelemetryTick(PluginTelemetryContext? context);

    /// <summary>
    /// Invoked when the panel is unloaded, disabled, or the host application is shutting down.
    /// Release unmanaged resources, stop timers, and save any local plugin state.
    /// </summary>
    void Shutdown();
}
