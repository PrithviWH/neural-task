using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using NeuralTask.Contracts;

namespace NeuralTask.App.Services;

/// <summary>
/// Custom AssemblyLoadContext for isolated plugin loading.
/// Routes contract and shared framework assemblies to the host context
/// while loading plugin-specific dependencies from the plugin directory.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _pluginDirectory = Path.GetDirectoryName(pluginPath)
            ?? throw new ArgumentException("Invalid plugin path", nameof(pluginPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Shared Contract Assembly: Must share the host's loaded type
        if (string.Equals(assemblyName.Name, "NeuralTask.Contracts", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(INeuralTaskPanel).Assembly;
        }

        // 2. Try loading from plugin directory first
        var localPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(localPath))
        {
            return LoadFromAssemblyPath(localPath);
        }

        // 3. Try loading from host base directory for shared assemblies
        var hostPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(hostPath))
        {
            return LoadFromAssemblyPath(hostPath);
        }

        // 4. Framework / BCL fall back to host Default context
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var localPath = Path.Combine(_pluginDirectory, $"{unmanagedDllName}.dll");
        if (File.Exists(localPath))
        {
            return LoadUnmanagedDllFromPath(localPath);
        }

        return IntPtr.Zero;
    }
}
