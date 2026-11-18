using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NeuralTask.Contracts;

namespace NeuralTask.App.Services;

/// <summary>
/// Discovers and manages external extension panels from a local plugins folder.
/// Enforces complete failure isolation so broken, malicious, or crashing plugins
/// never cause the host monitoring process to terminate.
/// </summary>
public class PluginManager : IPluginManager
{
    private readonly List<SafePluginPanel> _loadedPanels = new();
    private readonly List<PluginLoadContext> _activeContexts = new();
    private readonly object _lock = new();

    public IReadOnlyList<SafePluginPanel> LoadedPanels
    {
        get
        {
            lock (_lock)
            {
                return _loadedPanels.OrderBy(p => p.Order).ToList();
            }
        }
    }

    public string PluginsDirectory { get; private set; }

    public event Action<SafePluginPanel>? PanelLoaded;
    public event Action<SafePluginPanel>? PanelFaulted;

    public PluginManager(string? baseDirectory = null)
    {
        PluginsDirectory = baseDirectory
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
    }

    /// <summary>
    /// Scans the plugins folder for assemblies implementing <see cref="INeuralTaskPanel"/>.
    /// </summary>
    public void DiscoverAndLoadPlugins(string? customDirectory = null)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(customDirectory))
            {
                PluginsDirectory = customDirectory;
            }

            try
            {
                if (!Directory.Exists(PluginsDirectory))
                {
                    Directory.CreateDirectory(PluginsDirectory);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginManager] Failed to create plugins directory: {ex.Message}");
                return;
            }

            var dllFiles = new List<string>();
            try
            {
                // Find all DLLs directly in /plugins or in immediate subdirectories (e.g. /plugins/Gpu/)
                dllFiles.AddRange(Directory.GetFiles(PluginsDirectory, "*.dll", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginManager] Error enumerating plugins folder: {ex.Message}");
                return;
            }

            foreach (var dllPath in dllFiles)
            {
                LoadPluginAssembly(dllPath);
            }
        }
    }

    private void LoadPluginAssembly(string dllPath)
    {
        // Ignore host assemblies if they happen to be in the folder
        var fileName = Path.GetFileName(dllPath);
        if (fileName.Equals("NeuralTask.Contracts.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("NeuralTask.Core.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("NeuralTask.App.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("NeuralTask.Theming.dll", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PluginLoadContext? loadContext = null;
        Assembly? assembly = null;

        try
        {
            loadContext = new PluginLoadContext(dllPath);
            assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
        }
        catch (BadImageFormatException)
        {
            // Native or corrupted DLL — silently skip per PRD isolation design
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginManager] Failed to load assembly '{dllPath}': {ex.Message}");
            return;
        }

        if (assembly == null) return;

        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            // Some dependencies might be missing; salvage whatever types loaded
            exportedTypes = rtle.Types.Where(t => t != null).ToArray()!;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginManager] Failed to read types from '{dllPath}': {ex.Message}");
            return;
        }

        var panelContractType = typeof(INeuralTaskPanel);
        var panelTypes = exportedTypes
            .Where(t => t != null &&
                        panelContractType.IsAssignableFrom(t) &&
                        !t.IsAbstract &&
                        !t.IsInterface &&
                        t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();

        if (panelTypes.Count == 0) return;

        _activeContexts.Add(loadContext);

        foreach (var type in panelTypes)
        {
            try
            {
                var instance = Activator.CreateInstance(type) as INeuralTaskPanel;
                if (instance == null) continue;

                // Check for duplicate panel IDs
                if (_loadedPanels.Any(p => string.Equals(p.Id, instance.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginManager] Duplicate panel ID '{instance.Id}' skipped.");
                    instance.Dispose();
                    continue;
                }

                var safePanel = new SafePluginPanel(instance, dllPath);
                safePanel.FaultOccurred += p => PanelFaulted?.Invoke(p);

                // Run safe initialization
                safePanel.InitializeSafe();

                _loadedPanels.Add(safePanel);
                PanelLoaded?.Invoke(safePanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginManager] Failed to activate panel type '{type.FullName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Dispatches telemetry ticks to all registered safe panels.
    /// </summary>
    public void DispatchTelemetryTick(PluginTelemetryContext context)
    {
        SafePluginPanel[] snapshot;
        lock (_lock)
        {
            snapshot = _loadedPanels.ToArray();
        }

        foreach (var panel in snapshot)
        {
            panel.OnTelemetryTickSafe(context);
        }
    }

    /// <summary>
    /// Shuts down and releases all loaded plugin panels.
    /// </summary>
    public void UnloadAll()
    {
        lock (_lock)
        {
            foreach (var panel in _loadedPanels)
            {
                panel.ShutdownSafe();
                panel.Dispose();
            }
            _loadedPanels.Clear();

            foreach (var context in _activeContexts)
            {
                try
                {
                    context.Unload();
                }
                catch
                {
                }
            }
            _activeContexts.Clear();
        }
    }

    public void Dispose()
    {
        UnloadAll();
    }
}
