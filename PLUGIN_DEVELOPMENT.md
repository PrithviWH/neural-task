# NeuralTask // Plugin Development Guide (PRD §8.5)

Welcome to the NeuralTask plugin development guide! NeuralTask provides an extensible, failure-isolated plugin architecture that allows external developers to build custom dashboard widgets and detail-panel tabs without touching or recompiling the host application.

---

## 1. Architecture Overview

```
                      ┌────────────────────────────────────────┐
                      │            NeuralTask Host             │
                      │  (WPF Shell / Process Monitor Coordinator)
                      └───────────────────┬────────────────────┘
                                          │ Discovers *.dll
                                          ▼
                      ┌────────────────────────────────────────┐
                      │             PluginManager              │
                      │   - Scans /plugins directory           │
                      │   - Isolated AssemblyLoadContext       │
                      │   - Fault containment wrapper          │
                      └───────────────────┬────────────────────┘
                                          │ Instantiates & Wraps
                                          ▼
                      ┌────────────────────────────────────────┐
                      │            SafePluginPanel             │
                      │   - Intercepts all unhandled errors    │
                      │   - Renders HUD fault card on crash    │
                      │   - Protects host kernel loop          │
                      └───────────────────┬────────────────────┘
                                          │ Invokes interface
                                          ▼
                      ┌────────────────────────────────────────┐
                      │          INeuralTaskPanel DLL          │
                      │  (e.g., NeuralTask.Plugins.Gpu.dll)     │
                      └────────────────────────────────────────┘
```

### Core Tenets
1. **Minimal, Stable Contract**: Plugins depend solely on `NeuralTask.Contracts.dll`.
2. **Local Folder Loading**: In v0.3 / Tier 3, plugins are dropped directly into the local `plugins/` directory (or subdirectories like `plugins/MyPlugin/`). No remote marketplace or network downloads are permitted per PRD §7.
3. **Zero-Crash Fault Isolation**: If a plugin throws an unhandled exception during initialization, view creation, or periodic telemetry updates, the host app will **never** crash. Instead, the offending panel displays an inline holographic fault banner with exception diagnostics, retry actions, and disable options.

---

## 2. The Contract Interface: `INeuralTaskPanel`

Every plugin DLL implements `INeuralTaskPanel` defined in `NeuralTask.Contracts`:

```csharp
namespace NeuralTask.Contracts;

public interface INeuralTaskPanel : IDisposable
{
    // Unique lowercase identifier (e.g. "disk-radar", "network-geo")
    string Id { get; }

    // Human-readable title displayed in the navigation rail tooltip and panel header
    string Title { get; }

    // Brief explanation of the panel's purpose
    string Description { get; }

    // SVG / WPF StreamGeometry string rendered inside the navigation rail icon button
    string IconGeometry { get; }

    // Relative sort order in the navigation rail (100+ recommended)
    int Order { get; }

    // Invoked once when the plugin is loaded and verified
    void Initialize();

    // Creates and returns the root WPF visual element
    FrameworkElement CreateView();

    // Invoked every 1 second with a lightweight kernel telemetry snapshot
    void OnTelemetryTick(PluginTelemetryContext? context);

    // Invoked when the panel is unloaded or the host is shutting down
    void Shutdown();
}
```

### Telemetry Context: `PluginTelemetryContext`

Passed to `OnTelemetryTick` each second:

```csharp
public record PluginTelemetryContext(
    double CpuUsagePercent,       // Total CPU utilization (0.0 to 100.0)
    long TotalMemoryBytes,        // Total installed physical RAM in bytes
    long UsedMemoryBytes,         // Currently allocated physical RAM in bytes
    long AvailableMemoryBytes,    // Free physical RAM in bytes
    int ActiveProcessCount,       // Total number of running processes
    DateTime Timestamp            // Capture timestamp (UTC)
);
```

---

## 3. Step-by-Step: Creating a Plugin from Scratch

Follow these 5 steps to create and run your own plugin:

### Step 1: Scaffold a New Class Library

In your terminal or PowerShell:

```bash
dotnet new classlib -n NeuralTask.Plugins.NetworkRadar -f net8.0-windows
```

### Step 2: Configure `.csproj`

Open `NeuralTask.Plugins.NetworkRadar.csproj` and enable WPF support and reference `NeuralTask.Contracts`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the contracts assembly -->
    <Reference Include="NeuralTask.Contracts">
      <HintPath>..\..\NeuralTask.Contracts\bin\Debug\net8.0-windows\NeuralTask.Contracts.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- Optional: Automatically copy DLL to host plugins folder on build -->
  <Target Name="CopyToPlugins" AfterTargets="Build">
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)src\NeuralTask.App\bin\$(Configuration)\net8.0-windows\plugins\NetworkRadar\" />
  </Target>

</Project>
```

> [!TIP]
> Notice `<Private>false</Private>` on the `NeuralTask.Contracts` reference: the host supplies this assembly at runtime through the isolated `PluginLoadContext`.

### Step 3: Implement the Panel Class

Create `NetworkRadarPanel.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using NeuralTask.Contracts;

namespace NeuralTask.Plugins.NetworkRadar;

public class NetworkRadarPanel : INeuralTaskPanel
{
    private TextBlock? _statusText;

    public string Id => "network-radar";
    public string Title => "Network Radar";
    public string Description => "Monitors real-time packet telemetry and active network interfaces";

    // Standard vector geometry for a Wi-Fi / Radar dish icon
    public string IconGeometry => "M 12,2 C 6.48,2 2,6.48 2,12 C 2,17.52 6.48,22 12,22 C 17.52,22 22,17.52 22,12 M 12,6 C 8.69,6 6,8.69 6,12 M 12,9 C 10.34,9 9,10.34 9,12 M 12,11 A 1,1 0 1 1 12,13 A 1,1 0 1 1 12,11";

    public int Order => 100;

    public void Initialize()
    {
        // One-time setup: establish performance counters, query hardware, etc.
    }

    public FrameworkElement CreateView()
    {
        var border = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 20, 20, 32)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Margin = new Thickness(24)
        };

        _statusText = new TextBlock
        {
            Text = "RADAR SCANNER INITIALIZED // AWAITING TELEMETRY TICK...",
            FontSize = 14,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, 255)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };

        border.Child = _statusText;
        return border;
    }

    public void OnTelemetryTick(PluginTelemetryContext? context)
    {
        if (_statusText == null || context == null) return;

        // Marshalled to UI thread
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            _statusText.Text = $"NETWORK RADAR ONLINE\nCPU: {context.CpuUsagePercent:F1}%\nRAM: {context.UsedMemoryBytes / (1024 * 1024):N0} MB\nACTIVE PROCESSES: {context.ActiveProcessCount}\nTIMESTAMP: {context.Timestamp:T}";
        });
    }

    public void Shutdown()
    {
        _statusText = null;
    }

    public void Dispose()
    {
        Shutdown();
    }
}
```

### Step 4: Build Your Plugin

```bash
dotnet build -c Release
```

### Step 5: Test Locally

1. Copy your compiled output DLL (e.g. `NeuralTask.Plugins.NetworkRadar.dll`) into the host app's `plugins/` directory:
   ```
   NeuralTask.App/
     NeuralTask.App.exe
     plugins/
       NetworkRadar/
         NeuralTask.Plugins.NetworkRadar.dll
   ```
2. Launch `NeuralTask.App.exe`.
3. Notice your plugin icon appears automatically in the left navigation rail!
4. Click the icon to view your custom hosted panel with live telemetry updates.

---

## 4. First-Party Example Reference: `NeuralTask.Plugins.Gpu`

NeuralTask ships with an official first-party reference plugin:
- **Location**: [`src/plugins/NeuralTask.Plugins.Gpu`](file:///D:/My%20Projects/tech-project/src/plugins/NeuralTask.Plugins.Gpu)
- **Features Demonstrated**:
  - Windows Performance Counter integration (`\GPU Engine(*)\Utilization Percentage` and `\GPU Adapter Memory(*)\Dedicated Usage`).
  - WMI hardware querying (`Win32_VideoController`).
  - WPF MVVM architecture (`GpuPanelViewModel`, `GpuPanelView.xaml`).
  - Real-time rolling waveform sparkline calculation.
  - Holographic HUD theme integration (`BrushAccentPrimary`, `BrushPanelSurface`).

---

## 5. Fault Isolation and Best Practices

### Exception Containment
If your plugin throws an unhandled exception inside `Initialize()`, `CreateView()`, or `OnTelemetryTick()`, the host catches it immediately:
- The panel enters a **Faulted** state.
- An inline HUD error card replaces the panel view, displaying the exception type and message.
- The host continues running uninterrupted.
- Users can click **[ 🔄 RETRY INITIALIZATION ]** or disable the plugin from **Settings -> Extension Plugins**.

### Performance & Memory Rules
1. **Never block in `OnTelemetryTick`**: Telemetry ticks occur once per second. Do not perform heavy synchronous file I/O or network requests directly on this thread.
2. **Dispose unmanaged handles**: If using PerformanceCounters, WMI queries, or native Win32 handles, cleanly dispose them in `Shutdown()` or `Dispose()`.
3. **Use Vector Icons**: Provide vector Path data in `IconGeometry` for crisp rendering at all display DPI scalings.

---

## 6. Removing or Disabling Plugins

To remove a plugin:
1. Simply delete the plugin's folder or `.dll` file from the `/plugins` directory.
2. Restart NeuralTask or reload from the **Settings** panel.

No host registry keys or host configuration files need to be modified.
