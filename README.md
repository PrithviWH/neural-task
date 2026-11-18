# NeuralTask

> **A Futuristic AI-Hologram Process & System Monitor for Windows**

NeuralTask is an open-source native Windows task manager and system diagnostics utility featuring an "AI hologram / hacker HUD" command-center aesthetic, designed to deliver high visual polish alongside deep process-management capabilities (kill-tree, startup impact scoring, and local security heuristics).

---

## Tech Stack Decision

Per the project roadmap and environment capability checks:
* **Selected Framework:** **WPF on .NET 8 with [WPF-UI](https://github.com/lepoco/wpfui) (v4.3)**.
* **Context & Rationale:** WinUI 3 SDK templates were not installed in the target development environment. Falling back to WPF + WPF-UI provides significant advantages for NeuralTask's core objectives:
  1. **Native Modern Aesthetics:** Direct access to Windows 11 Mica and Acrylic window backdrops and fluent controls without legacy chrome.
  2. **Zero-Friction Portable Distribution:** Clean single-file self-contained `.exe` publishing (essential for GitHub releases) without unmanaged Windows App SDK bundling friction.
  3. **High Performance & Sub-Second Startup:** Low cold-start overhead (<1s) and minimal idle resource footprint, directly serving the PRD non-functional requirements.

---

## Solution Architecture

```
/src
  /NeuralTask.App         -> Main WPF executable, Mica/Acrylic HUD shell, DI (Microsoft.Extensions.DependencyInjection), ViewModels, Views
  /NeuralTask.Core        -> OS telemetry engine: ProcessMonitorService, SystemMetricsService, ProcessTreeBuilder, Win32 P/Invoke interop
  /NeuralTask.Theming     -> Centralized design tokens (Dark Hologram & Light Holo-Glass), brushes, fonts, theme switching engine
  /NeuralTask.Harness     -> Live terminal CLI harness for headless diagnostics and verification
/tests
  /NeuralTask.Core.Tests  -> Automated xUnit test suite (16 tests covering CPU deltas, orphan trees, collections, telemetry, buffers)
```

### Design Token Architecture (`NeuralTask.Theming`)
Color and typography tokens strictly adhere to PRD §13 specifications:
* **Dark (Hologram):** Deep space canvas (`#0A0E14`), translucent panel surface (`rgba(16,20,30,0.65)`), vibrant electric cyan accent (`#00E5FF`), and secondary violet/magenta (`#B14EFF`).
* **Light (Holo-Glass):** High-contrast cleanroom canvas (`#F4F6FA`), frosty acrylic (`rgba(255,255,255,0.65)`), deep cyan accent (`#0091B3`), and violet accent (`#7A2FD1`).
* **Zero-Hardcoding Guarantee:** The UI layer (`NeuralTask.App`) consumes semantic resource keys (`BrushBackgroundBase`, `BrushAccentPrimary`, `FontFamilyNumeric`, etc.) without hardcoded hex values.

---

## Live Monitoring Architecture (Phase 3)

### 1. Metric Accuracy & Hardware Measurement Sources
NeuralTask prioritizes metric precision over misleading approximations:
* **System CPU Load:** Measured via Win32 `GetSystemTimes` calculating genuine delta ratios:
  $$\text{CPU \%} = 1.0 - \frac{\Delta\text{IdleTime}}{\Delta\text{KernelTime} + \Delta\text{UserTime}}$$
* **Physical System Memory:** Measured via `GlobalMemoryStatusEx` (`MEMORYSTATUSEX`) reporting total physical RAM vs. committed load percentage.
* **System Network Throughput:** Computed from active IPv4 network interfaces via `NetworkInterface.GetAllNetworkInterfaces()`, aggregating differential byte receipt/transmission rates.
* **Process File/Disk I/O:** Derived from `GetProcessIoCounters` measuring delta `ReadTransferCount + WriteTransferCount`. Accurately displayed as **Process I/O Rate** rather than false physical disk transfer.
* **Per-Process Network Traffic:** Accurately surfaced as `—` (unavailable). Under Windows, measuring per-process network traffic requires a kernel NDIS filter driver or high-overhead ETW packet traces (`Microsoft-Windows-Kernel-Network`); Win32 `IO_COUNTERS.OtherTransferCount` measures device/socket control handles, NOT network bandwidth, and is never misrepresented as network traffic.
* **Process Working Set & CPU %:** Computed using `CreateToolhelp32Snapshot` and multi-sample `GetProcessTimes` deltas normalized across logical CPU cores.

### 2. Threading & Performance Model
* **Drop-Frame UI Throttling:** `ProcessMonitorCoordinator` utilizes non-blocking `Interlocked.CompareExchange` locks to dispatch snapshots onto the WPF UI thread. If the UI is rendering or processing an expensive layout pass when a new sample arrives, the coordinator drops the intermediate UI frame rather than queuing stale operations.
* **Recycled ObservableCollection Sync:** Instead of clearing and re-allocating 300+ UI row items on every tick, `ProcessListViewModel` executes an in-place collection synchronization algorithm (`SyncObservableCollection`) keyed by PID. Existing items update their bound properties in-place, preserving selection state, keyboard focus, and virtual scroll offsets.
* **Bounded History Ring Buffers:** Waveform chart data is stored in a fixed 60-sample ring buffer (`PerformanceHistoryBuffer`) generating normalized WPF `PointCollection`s without unbounded memory growth or garbage collector pressure.
* **Configurable Sampling Cadence:** The sampling interval can be dynamically adjusted between 500ms (High Performance), 1000ms (Balanced), and 2000ms (Power Saver) via Settings or JSON configuration (`%LOCALAPPDATA%\NeuralTask\settings.json`) without restarting the sampling loop.

---

## Quickstart: Build and Run in 4 Steps

1. **Clone the repository:**
   ```powershell
   git clone https://github.com/your-username/NeuralTask.git
   cd NeuralTask
   ```

2. **Restore dependencies:**
   ```powershell
   dotnet restore NeuralTask.sln
   ```

3. **Build the solution:**
   ```powershell
   dotnet build NeuralTask.sln --configuration Release
   ```

4. **Launch the application:**
   ```powershell
   dotnet run --project src/NeuralTask.App/NeuralTask.App.csproj
   ```

To execute unit tests:
```powershell
dotnet test NeuralTask.sln
```

---

## Roadmap Overview
* **v0.1 (Current Skeleton -> MVP):** Core theme, live process table (`NtQuerySystemInformation`), safe kill-tree with creation timestamp checks, real-time hardware graphs.
* **v0.2:** Event-Log-based startup impact score and local suspicious process heuristics.
* **v0.3:** Light "Holo-Glass" lab theme and process tree visualizer.
* **v0.4+:** Historical telemetry capture, JSON/CSV export, and plugin architecture.

---

## Licensing
Provisional open-source notice. License choice (MIT vs. GPLv3) is pending community alignment per PRD §12.
