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

### 3. Process Management & Termination (`ProcessActionService`)
* **Kill-Tree Verification:** NeuralTask validates parent/child start-time relationships before termination. If a parent process terminates and Windows recycles its PID, children spawned after the parent's exit are classified as orphans and never terminated by an unrelated recycled PID.
* **Neural Topology Graph:** For processes with child descendants, a specialized modal confirmation dialog visualizes the connected parent-child hierarchy with glowing neural node indicators before executing termination.
* **Non-Alarming Error Handling:** Terminations of protected system processes (e.g. `csrss.exe`, `System`) return structured `AccessDenied` results and present clean inline notification banners rather than throwing raw exception dialogs.

### 4. Fuzzy Subsequence Search
* **Gap-Tolerant Matching:** The search engine in `NeuralTask.Core` (`FuzzyMatcher`) performs sequential subsequence matching with gap tolerance (e.g. typing "chr" matches `chrome.exe`, "nt" matches `NeuralTask.App.exe`), alongside direct PID matching.
* **Zero UI Lag:** Filtering runs instantaneously across 300+ processes with in-place recycled UI synchronization.

### 5. System Tray Engine & Live Mini-Graph
* **Sub-Millisecond GDI+ Rendering:** Every sampling tick, `TrayIconService` draws an in-memory 16x16 sparkline bitmap displaying real-time CPU % (electric cyan) or RAM % (neon magenta) in under 0.2ms, recycling Win32 `HICON` handles with zero GDI leaks.
* **Hover Telemetry:** Hovering the tray icon displays a live summary tooltip with current CPU %, physical RAM consumed, and top-2 ranked processes.
* **Minimize-to-Tray:** Closing or minimizing the HUD window hides it to the notification area; clicking the tray icon restores and brings the window forward.

---

## Startup Impact Scoring Engine (Phase 5 / Tier 2)

### 1. Explainable 0–100 Impact Scoring (PRD §8.3)
Traditional Task Manager labels startup applications with subjective, opaque ratings ("Low", "Medium", "High") with no verifiable telemetry. NeuralTask replaces this with an objective, normalized **0–100 impact score** accompanied by a complete **Time vs. Resource decomposition**:
* **Time Score ($0-50$ points):** Measures process initialization duration and boot-contention delay during the post-logon window ($N=45$s). Early boot-phase launches incur a contention multiplier reflecting competition for storage cache and CPU scheduling:
  $$S_{\text{time}} = \min\left(50, \frac{\text{Duration}_{\text{active}}}{N} \times 40 \times M_{\text{contention}}\right)$$
* **Resource Score ($0-50$ points):** Measures physical hardware utilization, decomposed into CPU time ($0-30$ points) and cumulative disk/storage I/O ($0-20$ points):
  $$S_{\text{resource}} = \min\left(30, \frac{\text{CpuTimeSeconds}}{N \times 0.15} \times 30\right) + \min\left(20, \frac{\text{IoBytes}}{80\text{ MB}} \times 20\right)$$
* **Explainable Decomposition:** Every startup item provides an expandable breakdown displaying:
  * Two-tone proportional split meter (Cyan for Time, Violet/Magenta for Resource)
  * Initialization duration and boot delay in seconds
  * Process CPU core time consumption
  * Cumulative disk/file I/O throughput in MB
  * Plain-English explanation sentence (e.g. *"Active for 28.0s during logon window (5.0s post-boot delay), consuming 3.8s CPU time and 81.1 MB I/O."*)

### 2. Comprehensive Multi-Source Enumeration
The `StartupScoreEngine` aggregates and deduplicates startup hooks across:
1. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
2. `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`
3. `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run`
4. User Startup Folder (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`)
5. Common Startup Folder (`%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup`)
6. WMI `Win32_StartupCommand` (supplemental discovery)

### 3. Native Task Manager Binary Compatibility & State Toggling
* **Binary Flag Integration:** Enable/disable states are managed directly in Windows' native binary format at `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` and `HKLM\...` (`0x02` enabled, `0x03` disabled), maintaining 100% interoperability with Windows Task Manager without deleting registry entries.
* **Privilege & Elevation Handling (PRD §7):** NeuralTask operates smoothly under standard user privileges. Modifying machine-wide entries (HKLM) triggers a non-alarming administrative banner with a one-click **"RELAUNCH AS ADMIN"** action (`runas`).

### 4. Technical Architecture: v0.2 Fallback vs. Future ETW Boot Trace (PRD §11)
Per the explicit fallback in **PRD §11 (Technical Risks)**:
* In **v0.2**, startup impact is computed via active process correlation, uptime boot timing, and CPU/IO sampling measurement.
* A future **v0.3** upgrade will incorporate an optional offline `Microsoft-Windows-Kernel-Process` ETW session across reboots for kernel-exact boot trace metrics.

---

## Suspicious-Process Heuristic Engine (Phase 6 / Tier 2)

> **MANDATORY LEGAL & TECHNICAL DISCLAIMER (PRD §8.4 & §11):**  
> NeuralTask's heuristic flags are local diagnostic aids intended solely to highlight potential anomalies for user triage. This software is **EXPLICITLY NOT AN ANTIVIRUS, ANTIMALWARE, OR SECURITY PRODUCT**, does not provide real-time endpoint threat protection, and must never be relied upon as a substitute for certified antivirus software.

### 1. Transparent Local-Only Heuristic Rules (PRD §8.4)
The `HeuristicFlagEngine` executes 100% locally with zero cloud dependencies, network queries, or telemetric reporting, evaluating running processes across 4 transparent rules:

1. **Digital Signature Verification (`ISignatureVerifier`):**
   * Verifies PE Authenticode signatures using the native Windows `WinVerifyTrust` API (`WINTRUST_ACTION_GENERIC_VERIFY_V2`) configured for offline verification (`WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL`).
   * Validates full PKCS#7 certificate chains without tripping on missing intermediate certificates.
   * Recognizes catalog-signed Windows OS system components in `System32` and `Windows`.
   * Flags invalid, altered, or revoked certificates (`SIG_INVALID`).
2. **Unusual Execution Paths:**
   * Flags processes running from `%TEMP%` (`\AppData\Local\Temp`, `\Windows\Temp`) where droppers frequently execute (`PATH_TEMP`).
   * Flags binaries executing directly from the user's `Downloads` directory without standard installation (`PATH_DOWNLOADS`).
   * Identifies executables residing in `%APPDATA%` / `%LOCALAPPDATA%` (`PATH_APPDATA`).
   * **Developer-Friendly False-Positive Mitigation:** In Conservative mode, normal developer tools and package managers (`AppData\Local\Programs`, `npm`, `cargo`, `scoop`, `JetBrains`, `dotnet`) and digitally signed applications are exempted from AppData flags to prevent alert fatigue.
3. **Statistically High Handle Count:**
   * Dynamic statistical baseline: Computes the 95th percentile ($P_{95}$) handle count across the active snapshot population.
   * Dual-baseline noise floors: User-mode applications use a conservative floor (4,000 handles in Conservative, 2,500 in Standard), while core Windows system services (`explorer.exe`, `svchost.exe`) have an elevated baseline (15,000 handles) reflecting legitimate OS-wide handle aggregation.
   * Kernel processes ($\text{PID} \le 4$, e.g. System, Idle) and access-denied services are excluded to prevent false alarms.
4. **System Process Name Mimicry:**
   * **Typo-Squatting Detection:** Computes Levenshtein edit distance ($1 \le d \le 2$) against core Windows system binaries (e.g. flagging `"svch0st.exe"`, `"csrs.exe"`, `"lsasss.exe"`).
   * **System Binary Masquerade:** Identifies processes using genuine system names (e.g. `"svchost.exe"`, `"lsass.exe"`) that execute outside `C:\Windows\System32` or `C:\Windows`.

### 2. Explainable Human-Readable Reasons (Never Bare Booleans)
Every flag carries an explicit, human-readable reason string surfaced directly to the user:
* *"Binary is not digitally signed and runs from an untrusted location"*
* *"Process executable resides in temporary directory: C:\Users\...\Temp\payload.exe"*
* *"Process name 'svch0st.exe' closely mimics Windows system binary 'svchost.exe' (Levenshtein distance: 1)"*
* *"Process holds 6,420 handles, exceeding statistical 95th percentile baseline of 4,000"*

### 3. UI Presentation & Holographic Scanner Sweep (PRD §6.2 & §9)
* **Subtle Glowing Badges:** Anomaly indicators render as subtle amber glowing pills (`[ ⚠ 1 FLAG ]` / `[ ⚠ 2 FLAGS ]`), strictly avoiding alarmist red banners.
* **Rich Tooltips:** Hovering or tapping a badge displays the exact rule titles and reason explanations.
* **Persistent Triage Bar:** Process Matrix displays a persistent non-alarming status strip (`TRIAGE: X ANOMALIES`) with the mandatory non-AV notice.
* **Manual Re-Scan Sweep:** Clicking **"RE-SCAN HEURISTICS"** triggers a GPU-composited laser light-beam sweep animation across the process table while refreshing evaluations.
* **User-Configurable Sensitivity:** Settings allows users to select between **Conservative (Dev-Friendly)** [Default], **Standard**, and **Aggressive** sensitivity profiles.

---

## Roadmap Overview
* **v0.1 (Released / Public RC):** Core holographic & holo-glass themes, live process table with multi-column sorting, fuzzy search, safe kill-tree with creation timestamp checks, neural topology dialog, real-time hardware graphs, and persistent system tray mini-graph.
* **v0.2 (Current):** Explainable 0-100 startup impact scoring with Time vs. Resource split, multi-source enumeration, binary `StartupApproved` toggle, admin elevation handling, and local-only explainable suspicious-process heuristic triage (PRD §8.4).
* **v0.3:** Deep process tree visualizer, ETW boot-trace capture across reboots, and historical telemetry logging.
* **v0.4+:** Historical telemetry capture, JSON/CSV export, and plugin architecture.

---

## Licensing
Provisional open-source notice. License choice (MIT vs. GPLv3) is pending community alignment per PRD §12.
