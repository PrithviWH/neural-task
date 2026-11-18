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
  /NeuralTask.App         -> Main WPF executable, Mica-enabled HUD window, DI & UI routing
  /NeuralTask.Core        -> Shared business logic, telemetry models, and future OS interop
  /NeuralTask.Theming     -> Centralized design tokens (Dark Hologram & Light Holo-Glass), brushes, fonts
/tests
  /NeuralTask.Core.Tests  -> Automated xUnit test suite for telemetry and process engines
```

### Design Token Architecture (`NeuralTask.Theming`)
Color and typography tokens strictly adhere to PRD §13 specifications:
* **Dark (Hologram):** Deep space canvas (`#0A0E14`), translucent panel surface (`rgba(16,20,30,0.65)`), vibrant electric cyan accent (`#00E5FF`), and secondary violet/magenta (`#B14EFF`).
* **Light (Holo-Glass):** High-contrast cleanroom canvas (`#F4F6FA`), frosty acrylic (`rgba(255,255,255,0.65)`), deep cyan accent (`#0091B3`), and violet accent (`#7A2FD1`).
* **Zero-Hardcoding Guarantee:** The UI layer (`NeuralTask.App`) consumes semantic resource keys (`BrushBackgroundBase`, `BrushAccentPrimary`, `FontFamilyNumeric`, etc.) without hardcoded hex values.

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
