using System;
using System.Collections.Generic;
using NeuralTask.Contracts;

namespace NeuralTask.App.Services;

/// <summary>
/// Service contract for discovering, safely loading, and orchestrating extension plugins.
/// </summary>
public interface IPluginManager : IDisposable
{
    /// <summary>
    /// All discovered and active plugin panels.
    /// </summary>
    IReadOnlyList<SafePluginPanel> LoadedPanels { get; }

    /// <summary>
    /// Absolute path to the local plugins root directory.
    /// </summary>
    string PluginsDirectory { get; }

    /// <summary>
    /// Raised when a new plugin panel is successfully discovered and loaded.
    /// </summary>
    event Action<SafePluginPanel>? PanelLoaded;

    /// <summary>
    /// Raised when a plugin encounters a runtime fault or unhandled exception.
    /// </summary>
    event Action<SafePluginPanel>? PanelFaulted;

    /// <summary>
    /// Scans the plugins directory and loads all valid <see cref="INeuralTaskPanel"/> implementations.
    /// </summary>
    /// <param name="customDirectory">Optional custom directory path for testing or override.</param>
    void DiscoverAndLoadPlugins(string? customDirectory = null);

    /// <summary>
    /// Dispatches telemetry ticks to all loaded and active panels.
    /// </summary>
    /// <param name="context">Telemetry metrics snapshot.</param>
    void DispatchTelemetryTick(PluginTelemetryContext context);

    /// <summary>
    /// Safely shuts down and disposes all loaded plugin panels.
    /// </summary>
    void UnloadAll();
}
