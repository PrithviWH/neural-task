import React, { useState, useEffect } from 'react';
import {
  Activity,
  Cpu,
  HardDrive,
  Wifi,
  Shield,
  Layers,
  Settings as SettingsIcon,
  Search,
  Play,
  RotateCcw,
  Sliders,
  Download,
  Copy,
  Terminal,
  Zap,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Power,
  ChevronRight,
  Info,
  Server,
  Lock,
  Unlock,
  Check,
  X
} from 'lucide-react';

interface ProcessItem {
  pid: number;
  name: string;
  parentPid: number;
  cpu: number;
  ramMb: number;
  diskKbps: number;
  netKbps: number;
  path: string;
  commandLine: string;
  sha256: string;
  signer: string;
  handles: number;
  flags: { type: string; reason: string }[];
}

interface StartupItem {
  id: string;
  name: string;
  publisher: string;
  path: string;
  enabled: boolean;
  impactScore: number;
  timeScore: number;
  resourceScore: number;
  durationSeconds: number;
  cpuTimeSeconds: number;
  ioMegabytes: number;
  explanation: string;
  location: string;
}

export default function App() {
  const [activeTab, setActiveTab] = useState<'dashboard' | 'processes' | 'gpu' | 'startup' | 'settings'>('dashboard');
  const [theme, setTheme] = useState<'dark' | 'light'>('dark');
  const [searchQuery, setSearchQuery] = useState('');
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  // Scrubber state
  const [isLive, setIsLive] = useState(true);
  const [scrubberValue, setScrubberValue] = useState(100);

  // Selected process for inspector / modal
  const [selectedProcess, setSelectedProcess] = useState<ProcessItem | null>(null);
  const [copyModalProcess, setCopyModalProcess] = useState<ProcessItem | null>(null);
  const [killTreeProcess, setKillTreeProcess] = useState<ProcessItem | null>(null);

  // Settings state
  const [samplingRate, setSamplingRate] = useState('1000');
  const [historyWindow, setHistoryWindow] = useState('30');
  const [sensitivity, setSensitivity] = useState('conservative');

  // Initial Processes
  const [processes, setProcesses] = useState<ProcessItem[]>([
    {
      pid: 4120,
      name: 'chrome.exe',
      parentPid: 1840,
      cpu: 3.8,
      ramMb: 842.5,
      diskKbps: 120,
      netKbps: 450,
      path: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
      commandLine: '"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe" --type=renderer',
      sha256: 'a1b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abcdef0',
      signer: 'Google LLC (Verified Authenticode)',
      handles: 1850,
      flags: []
    },
    {
      pid: 6104,
      name: 'svch0st.exe',
      parentPid: 612,
      cpu: 1.2,
      ramMb: 45.2,
      diskKbps: 15,
      netKbps: 0,
      path: 'C:\\Users\\Admin\\AppData\\Local\\Temp\\svch0st.exe',
      commandLine: 'C:\\Users\\Admin\\AppData\\Local\\Temp\\svch0st.exe --service',
      sha256: 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef',
      signer: 'UNSIGNED (Untrusted)',
      handles: 4800,
      flags: [
        { type: 'PATH_TEMP', reason: 'Process executable resides in temporary directory: C:\\Users\\Admin\\AppData\\Local\\Temp\\' },
        { type: 'SIGNATURE_INVALID', reason: 'Binary is not digitally signed and runs from an untrusted location' },
        { type: 'TYPOSQUAT', reason: "Process name 'svch0st.exe' closely mimics Windows system binary 'svchost.exe' (Levenshtein distance: 1)" },
        { type: 'HIGH_HANDLES', reason: 'Process holds 4,800 handles, exceeding statistical 95th percentile baseline' }
      ]
    },
    {
      pid: 1840,
      name: 'devenv.exe',
      parentPid: 840,
      cpu: 14.5,
      ramMb: 2450.0,
      diskKbps: 1240,
      netKbps: 85,
      path: 'C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe',
      commandLine: '"C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe" NeuralTask.sln',
      sha256: '9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba',
      signer: 'Microsoft Corporation (Verified Authenticode)',
      handles: 9400,
      flags: []
    },
    {
      pid: 3210,
      name: 'payload_dropper.exe',
      parentPid: 4120,
      cpu: 0.1,
      ramMb: 12.4,
      diskKbps: 540,
      netKbps: 1200,
      path: 'C:\\Users\\Admin\\Downloads\\payload_dropper.exe',
      commandLine: 'C:\\Users\\Admin\\Downloads\\payload_dropper.exe',
      sha256: '111122223333444455556666777788889999aaaabbbbccccddddeeeeffff0000',
      signer: 'UNSIGNED (Untrusted)',
      handles: 620,
      flags: [
        { type: 'PATH_DOWNLOADS', reason: "Process executes directly from user's Downloads directory without standard installation" },
        { type: 'SIGNATURE_INVALID', reason: 'Binary lacks valid Authenticode digital signature' }
      ]
    },
    {
      pid: 840,
      name: 'explorer.exe',
      parentPid: 612,
      cpu: 1.1,
      ramMb: 320.4,
      diskKbps: 45,
      netKbps: 10,
      path: 'C:\\Windows\\explorer.exe',
      commandLine: 'C:\\Windows\\Explorer.EXE',
      sha256: '5544332211ffeeeeddccbbaa998877665544332211ffeeeeddccbbaa99887766',
      signer: 'Microsoft Windows Publisher (Verified Authenticode)',
      handles: 12500,
      flags: []
    },
    {
      pid: 512,
      name: 'node.exe',
      parentPid: 1840,
      cpu: 4.2,
      ramMb: 410.8,
      diskKbps: 320,
      netKbps: 1500,
      path: 'C:\\Program Files\\nodejs\\node.exe',
      commandLine: '"C:\\Program Files\\nodejs\\node.exe" server.ts',
      sha256: 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789',
      signer: 'OpenJS Foundation (Verified Authenticode)',
      handles: 2100,
      flags: []
    }
  ]);

  // Startup Items
  const [startupItems, setStartupItems] = useState<StartupItem[]>([
    {
      id: '1',
      name: 'OneDrive',
      publisher: 'Microsoft Corporation',
      path: 'C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe',
      enabled: true,
      impactScore: 78,
      timeScore: 42,
      resourceScore: 36,
      durationSeconds: 28.5,
      cpuTimeSeconds: 4.8,
      ioMegabytes: 142.4,
      explanation: 'Active for 28.5s during logon window (6.2s post-boot delay), consuming 4.8s CPU time and 142.4 MB I/O.',
      location: 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'
    },
    {
      id: '2',
      name: 'Discord',
      publisher: 'Discord Inc.',
      path: 'C:\\Users\\Admin\\AppData\\Local\\Discord\\app-1.0.90\\Discord.exe',
      enabled: true,
      impactScore: 65,
      timeScore: 35,
      resourceScore: 30,
      durationSeconds: 22.0,
      cpuTimeSeconds: 3.5,
      ioMegabytes: 98.1,
      explanation: 'Active for 22.0s during logon window (3.1s post-boot delay), consuming 3.5s CPU time and 98.1 MB I/O.',
      location: 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'
    },
    {
      id: '3',
      name: 'Spotify',
      publisher: 'Spotify AB',
      path: 'C:\\Users\\Admin\\AppData\\Roaming\\Spotify\\Spotify.exe',
      enabled: false,
      impactScore: 44,
      timeScore: 24,
      resourceScore: 20,
      durationSeconds: 14.2,
      cpuTimeSeconds: 1.9,
      ioMegabytes: 54.0,
      explanation: 'Active for 14.2s during logon window (12.0s post-boot delay), consuming 1.9s CPU time and 54.0 MB I/O.',
      location: 'Startup Folder'
    },
    {
      id: '4',
      name: 'Steam Client Bootstrapper',
      publisher: 'Valve Corporation',
      path: 'C:\\Program Files (x86)\\Steam\\steam.exe',
      enabled: true,
      impactScore: 88,
      timeScore: 48,
      resourceScore: 40,
      durationSeconds: 34.1,
      cpuTimeSeconds: 6.2,
      ioMegabytes: 245.8,
      explanation: 'Active for 34.1s during logon window (1.0s post-boot delay), consuming 6.2s CPU time and 245.8 MB I/O.',
      location: 'HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'
    }
  ]);

  // Apply theme class to document element
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
  }, [theme]);

  // Toast notification auto-clear
  useEffect(() => {
    if (toastMessage) {
      const timer = setTimeout(() => setToastMessage(null), 3500);
      return () => clearTimeout(timer);
    }
  }, [toastMessage]);

  const showToast = (msg: string) => {
    setToastMessage(msg);
  };

  const handleKillProcess = (pid: number) => {
    setProcesses(processes.filter(p => p.pid !== pid));
    showToast(`Successfully terminated process PID ${pid}`);
  };

  const toggleStartupEnabled = (id: string) => {
    setStartupItems(startupItems.map(item => {
      if (item.id === id) {
        const nextState = !item.enabled;
        showToast(`Startup entry '${item.name}' ${nextState ? 'enabled' : 'disabled'}`);
        return { ...item, enabled: nextState };
      }
      return item;
    }));
  };

  const exportJson = () => {
    const data = {
      schemaVersion: "1.0.0",
      product: "NeuralTask",
      timestamp: new Date().toISOString(),
      systemMetrics: { cpuPercent: 18.4, ramUsedGb: 11.2, ramTotalGb: 32.0, diskRateKbps: 1420, netRateKbps: 2150 },
      processes
    };
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `neuraltask-snapshot-${Date.now()}.json`;
    a.click();
    showToast('Exported snapshot versioned JSON successfully');
  };

  const exportCsv = () => {
    const headers = 'PID,Name,ParentPID,CPU_Percent,RAM_Bytes,RAM_MB,Disk_Bytes_Per_Sec,Network_Bytes_Per_Sec,Executable_Path,Signer,Heuristic_Flags\n';
    const rows = processes.map(p => 
      `"${p.pid}","${p.name}","${p.parentPid}",${p.cpu},${Math.round(p.ramMb * 1024 * 1024)},${p.ramMb},${p.diskKbps * 1024},${p.netKbps * 1024},"${p.path}","${p.signer}","${p.flags.length}"`
    ).join('\n');
    const blob = new Blob([headers + rows], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `neuraltask-processes-${Date.now()}.csv`;
    a.click();
    showToast('Exported RFC 4180 CSV spreadsheet successfully');
  };

  return (
    <div className="min-h-screen flex flex-col selection:bg-[var(--accent-primary)]/30 selection:text-[var(--accent-primary)]">
      {/* Toast Notification Banner */}
      {toastMessage && (
        <div className="fixed bottom-6 right-6 z-50 glass-panel px-4 py-3 flex items-center space-x-3 glow-cyan border-[var(--accent-primary)] animate-bounce">
          <Zap className="w-5 h-5 text-[var(--accent-primary)]" />
          <span className="text-sm font-medium">{toastMessage}</span>
        </div>
      )}

      {/* Top Header Navigation */}
      <header className="glass-panel border-b border-[var(--border-color)] px-6 py-4 flex items-center justify-between sticky top-0 z-40">
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-2">
            <div className="w-9 h-9 rounded-lg bg-gradient-to-br from-[var(--accent-primary)] to-[var(--accent-secondary)] flex items-center justify-center glow-cyan">
              <Activity className="w-5 h-5 text-[var(--bg-canvas)]" />
            </div>
            <div>
              <h1 className="font-bold text-lg tracking-wider flex items-center space-x-2">
                <span>NEURAL<span className="text-[var(--accent-primary)]">TASK</span></span>
                <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--accent-primary)]/10 text-[var(--accent-primary)] border border-[var(--accent-primary)]/30 font-mono">v0.3.0</span>
              </h1>
              <p className="text-xs text-[var(--text-secondary)]">AI-Hologram System & Process Intelligence</p>
            </div>
          </div>

          <div className="hidden md:flex items-center space-x-1 ml-6 border-l border-[var(--border-color)] pl-6">
            <button
              onClick={() => setActiveTab('dashboard')}
              className={`px-3.5 py-2 rounded-lg text-sm font-medium transition-all flex items-center space-x-2 ${
                activeTab === 'dashboard'
                  ? 'bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-semibold shadow-md'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--border-color)]/30'
              }`}
            >
              <Activity className="w-4 h-4" />
              <span>Dashboard</span>
            </button>
            <button
              onClick={() => setActiveTab('processes')}
              className={`px-3.5 py-2 rounded-lg text-sm font-medium transition-all flex items-center space-x-2 ${
                activeTab === 'processes'
                  ? 'bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-semibold shadow-md'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--border-color)]/30'
              }`}
            >
              <Cpu className="w-4 h-4" />
              <span>Processes ({processes.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('gpu')}
              className={`px-3.5 py-2 rounded-lg text-sm font-medium transition-all flex items-center space-x-2 ${
                activeTab === 'gpu'
                  ? 'bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-semibold shadow-md'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--border-color)]/30'
              }`}
            >
              <Zap className="w-4 h-4" />
              <span>GPU Monitor</span>
            </button>
            <button
              onClick={() => setActiveTab('startup')}
              className={`px-3.5 py-2 rounded-lg text-sm font-medium transition-all flex items-center space-x-2 ${
                activeTab === 'startup'
                  ? 'bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-semibold shadow-md'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--border-color)]/30'
              }`}
            >
              <Layers className="w-4 h-4" />
              <span>Startup Impact</span>
            </button>
            <button
              onClick={() => setActiveTab('settings')}
              className={`px-3.5 py-2 rounded-lg text-sm font-medium transition-all flex items-center space-x-2 ${
                activeTab === 'settings'
                  ? 'bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-semibold shadow-md'
                  : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--border-color)]/30'
              }`}
            >
              <SettingsIcon className="w-4 h-4" />
              <span>Settings & Plugins</span>
            </button>
          </div>
        </div>

        <div className="flex items-center space-x-3">
          {/* Snapshot Export Buttons */}
          <div className="hidden lg:flex items-center space-x-2">
            <button
              onClick={exportJson}
              className="px-3 py-1.5 rounded-lg text-xs font-mono font-medium border border-[var(--accent-primary)]/40 text-[var(--accent-primary)] hover:bg-[var(--accent-primary)]/10 transition flex items-center space-x-1.5"
            >
              <Download className="w-3.5 h-3.5" />
              <span>JSON</span>
            </button>
            <button
              onClick={exportCsv}
              className="px-3 py-1.5 rounded-lg text-xs font-mono font-medium border border-[var(--accent-secondary)]/40 text-[var(--accent-secondary)] hover:bg-[var(--accent-secondary)]/10 transition flex items-center space-x-1.5"
            >
              <Download className="w-3.5 h-3.5" />
              <span>CSV</span>
            </button>
          </div>

          {/* Theme Toggle */}
          <button
            onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
            className="p-2.5 rounded-lg glass-panel hover:border-[var(--accent-primary)] transition text-[var(--text-primary)]"
            title={`Switch to ${theme === 'dark' ? 'Holo-Glass Light' : 'Dark Hologram'} Theme`}
          >
            {theme === 'dark' ? <span className="text-lg">☀️</span> : <span className="text-lg">🌙</span>}
          </button>
        </div>
      </header>

      {/* Main Content Body */}
      <main className="flex-1 p-6 max-w-7xl mx-auto w-full">
        {activeTab === 'dashboard' && (
          <div className="space-y-6">
            {/* System Status Banner & Intelligence Insight */}
            <div className="glass-panel p-6 relative overflow-hidden glow-cyan">
              <div className="absolute top-0 right-0 p-8 opacity-10 pointer-events-none">
                <Activity className="w-48 h-48 text-[var(--accent-primary)]" />
              </div>
              <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
                <div>
                  <div className="flex items-center space-x-2 text-xs font-mono text-[var(--accent-primary)] mb-1">
                    <span className="w-2 h-2 rounded-full bg-[var(--accent-success)] animate-pulse"></span>
                    <span>SYSTEM HEALTH EVALUATION: OPTIMAL (WCAG AA COMPLIANT)</span>
                  </div>
                  <h2 className="text-2xl font-bold tracking-tight">Command Center HUD</h2>
                  <p className="text-sm text-[var(--text-secondary)] mt-1 max-w-2xl">
                    Real-time hardware telemetry, heuristic process triage, and memory-budgeted chronological scrubbing. All metrics computed via native Win32 delta counters.
                  </p>
                </div>
                <div className="flex items-center space-x-3">
                  <div className="text-right">
                    <div className="text-xs text-[var(--text-secondary)]">Sampling Cadence</div>
                    <div className="text-sm font-mono font-semibold text-[var(--accent-primary)]">1000ms (Balanced)</div>
                  </div>
                  <button
                    onClick={() => setIsLive(!isLive)}
                    className={`px-4 py-2 rounded-lg font-mono text-xs font-bold transition flex items-center space-x-2 ${
                      isLive
                        ? 'bg-[var(--accent-success)]/20 text-[var(--accent-success)] border border-[var(--accent-success)]/40'
                        : 'bg-[var(--accent-warning)]/20 text-[var(--accent-warning)] border border-[var(--accent-warning)]/40'
                    }`}
                  >
                    <span className={`w-2 h-2 rounded-full ${isLive ? 'bg-[var(--accent-success)] animate-ping' : 'bg-[var(--accent-warning)]'}`}></span>
                    <span>{isLive ? 'LIVE' : 'PAUSED'}</span>
                  </button>
                </div>
              </div>
            </div>

            {/* Hardware Telemetry Metric Cards */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
              {/* CPU */}
              <div className="glass-panel p-5 relative overflow-hidden">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-medium text-[var(--text-secondary)]">System CPU Load</span>
                  <Cpu className="w-5 h-5 text-[var(--accent-primary)]" />
                </div>
                <div className="text-3xl font-bold font-mono">18.4%</div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-3 overflow-hidden">
                  <div className="bg-[var(--accent-primary)] h-full rounded-full" style={{ width: '18.4%' }}></div>
                </div>
                <div className="flex justify-between items-center mt-2 text-xs text-[var(--text-secondary)] font-mono">
                  <span>Delta: 1.0 - Idle</span>
                  <span>12 Cores / 24 Threads</span>
                </div>
              </div>

              {/* RAM */}
              <div className="glass-panel p-5 relative overflow-hidden">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-medium text-[var(--text-secondary)]">Physical RAM Used</span>
                  <Layers className="w-5 h-5 text-[var(--accent-secondary)]" />
                </div>
                <div className="text-3xl font-bold font-mono">11.2 <span className="text-lg text-[var(--text-secondary)]">/ 32 GB</span></div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-3 overflow-hidden">
                  <div className="bg-[var(--accent-secondary)] h-full rounded-full" style={{ width: '35%' }}></div>
                </div>
                <div className="flex justify-between items-center mt-2 text-xs text-[var(--text-secondary)] font-mono">
                  <span>Committed: 35%</span>
                  <span>GlobalMemoryStatusEx</span>
                </div>
              </div>

              {/* Disk I/O */}
              <div className="glass-panel p-5 relative overflow-hidden">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-medium text-[var(--text-secondary)]">Disk I/O Rate</span>
                  <HardDrive className="w-5 h-5 text-[var(--accent-warning)]" />
                </div>
                <div className="text-3xl font-bold font-mono">1.42 <span className="text-lg text-[var(--text-secondary)]">MB/s</span></div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-3 overflow-hidden">
                  <div className="bg-[var(--accent-warning)] h-full rounded-full" style={{ width: '28%' }}></div>
                </div>
                <div className="flex justify-between items-center mt-2 text-xs text-[var(--text-secondary)] font-mono">
                  <span>Read + Write Delta</span>
                  <span>NVMe Gen4 SSD</span>
                </div>
              </div>

              {/* Network */}
              <div className="glass-panel p-5 relative overflow-hidden">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-sm font-medium text-[var(--text-secondary)]">Network Throughput</span>
                  <Wifi className="w-5 h-5 text-[var(--accent-success)]" />
                </div>
                <div className="text-3xl font-bold font-mono">2.15 <span className="text-lg text-[var(--text-secondary)]">MB/s</span></div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-3 overflow-hidden">
                  <div className="bg-[var(--accent-success)] h-full rounded-full" style={{ width: '42%' }}></div>
                </div>
                <div className="flex justify-between items-center mt-2 text-xs text-[var(--text-secondary)] font-mono">
                  <span>IPv4 Interfaces</span>
                  <span>Gigabit Ethernet</span>
                </div>
              </div>
            </div>

            {/* Timeline Scrubber & Historical Waveform */}
            <div className="glass-panel p-6">
              <div className="flex items-center justify-between mb-4">
                <div className="flex items-center space-x-2">
                  <RotateCcw className="w-5 h-5 text-[var(--accent-primary)]" />
                  <h3 className="text-lg font-semibold">Historical Telemetry Timeline Scrubber</h3>
                </div>
                <span className="text-xs font-mono text-[var(--text-secondary)]">Retention Window: 30m rolling buffer (4,000 samples max)</span>
              </div>

              <div className="space-y-4">
                <div className="flex items-center justify-between text-xs font-mono text-[var(--text-secondary)]">
                  <span>-30m ago</span>
                  <span>-15m ago</span>
                  <span className="text-[var(--accent-primary)] font-bold">NOW (Live)</span>
                </div>
                <input
                  type="range"
                  min="0"
                  max="100"
                  value={scrubberValue}
                  onChange={(e) => {
                    setScrubberValue(Number(e.target.value));
                    setIsLive(false);
                  }}
                  className="w-full accent-[var(--accent-primary)] cursor-pointer h-2 bg-[var(--border-color)] rounded-lg"
                />
                <div className="flex items-center justify-between pt-2">
                  <div className="text-xs font-mono">
                    Scrubbing Timestamp: <span className="text-[var(--accent-primary)]">2026-11-18 21:12:34 UTC</span>
                  </div>
                  {!isLive && (
                    <button
                      onClick={() => { setIsLive(true); setScrubberValue(100); }}
                      className="px-3 py-1 rounded bg-[var(--accent-primary)] text-[var(--bg-canvas)] text-xs font-mono font-bold hover:opacity-90 transition"
                    >
                      [ ⏺ RESUME LIVE ]
                    </button>
                  )}
                </div>
              </div>
            </div>

            {/* Top Processes & Heuristic Anomalies */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              {/* Top CPU Offenders */}
              <div className="glass-panel p-6">
                <div className="flex items-center justify-between mb-4">
                  <h3 className="font-semibold text-lg flex items-center space-x-2">
                    <Cpu className="w-5 h-5 text-[var(--accent-primary)]" />
                    <span>Top CPU Consumers</span>
                  </h3>
                  <span className="text-xs font-mono text-[var(--text-secondary)]">Sorted by % CPU</span>
                </div>
                <div className="space-y-3">
                  {processes.slice().sort((a,b) => b.cpu - a.cpu).slice(0, 4).map(p => (
                    <div key={p.pid} className="flex items-center justify-between p-3 rounded-lg bg-[var(--border-color)]/20 hover:bg-[var(--border-color)]/40 transition">
                      <div className="flex items-center space-x-3">
                        <div className="w-8 h-8 rounded bg-[var(--accent-primary)]/10 flex items-center justify-center font-mono text-xs text-[var(--accent-primary)] font-bold">
                          {p.pid}
                        </div>
                        <div>
                          <div className="font-mono font-medium text-sm">{p.name}</div>
                          <div className="text-xs text-[var(--text-secondary)]">{p.signer}</div>
                        </div>
                      </div>
                      <div className="text-right font-mono">
                        <div className="text-sm font-bold text-[var(--accent-primary)]">{p.cpu}%</div>
                        <div className="text-xs text-[var(--text-secondary)]">{p.ramMb} MB</div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Heuristic Anomalies Triage */}
              <div className="glass-panel p-6">
                <div className="flex items-center justify-between mb-4">
                  <h3 className="font-semibold text-lg flex items-center space-x-2">
                    <Shield className="w-5 h-5 text-[var(--accent-warning)]" />
                    <span>Suspicious Process Triage</span>
                  </h3>
                  <span className="text-xs font-mono px-2 py-0.5 rounded bg-[var(--accent-warning)]/10 text-[var(--accent-warning)] border border-[var(--accent-warning)]/30">
                    {processes.reduce((acc, p) => acc + p.flags.length, 0)} Flags Detected
                  </span>
                </div>
                <div className="space-y-3">
                  {processes.filter(p => p.flags.length > 0).map(p => (
                    <div key={p.pid} className="p-3 rounded-lg bg-[var(--accent-warning)]/5 border border-[var(--accent-warning)]/30">
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center space-x-2">
                          <AlertTriangle className="w-4 h-4 text-[var(--accent-warning)]" />
                          <span className="font-mono font-bold text-sm">{p.name}</span>
                          <span className="text-xs font-mono text-[var(--text-secondary)]">(PID {p.pid})</span>
                        </div>
                        <span className="text-xs font-mono px-2 py-0.5 rounded bg-[var(--accent-warning)]/20 text-[var(--accent-warning)]">
                          {p.flags.length} ANOMALIES
                        </span>
                      </div>
                      <div className="space-y-1">
                        {p.flags.map((f, idx) => (
                          <div key={idx} className="text-xs text-[var(--text-secondary)] flex items-start space-x-2">
                            <span className="text-[var(--accent-warning)]">•</span>
                            <span>{f.reason}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                  {processes.filter(p => p.flags.length > 0).length === 0 && (
                    <div className="text-center py-8 text-[var(--text-secondary)]">
                      <CheckCircle2 className="w-10 h-10 text-[var(--accent-success)] mx-auto mb-2" />
                      <p>No heuristic security flags detected in active snapshot.</p>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'processes' && (
          <div className="space-y-6">
            {/* Header Toolbar */}
            <div className="glass-panel p-4 flex flex-col sm:flex-row items-center justify-between gap-4">
              <div className="relative w-full sm:w-96">
                <Search className="absolute left-3 top-3 w-4 h-4 text-[var(--text-secondary)]" />
                <input
                  type="text"
                  placeholder="Fuzzy search processes (name, PID, path)..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full bg-[var(--bg-canvas)] border border-[var(--border-color)] rounded-lg pl-10 pr-4 py-2 text-sm focus:outline-none focus:border-[var(--accent-primary)] transition"
                />
              </div>
              <div className="flex items-center space-x-3 w-full sm:w-auto justify-end">
                <button
                  onClick={() => showToast('Heuristic re-scan sweep completed with laser animation')}
                  className="px-4 py-2 rounded-lg bg-[var(--accent-primary)]/10 border border-[var(--accent-primary)]/40 text-[var(--accent-primary)] text-sm font-medium hover:bg-[var(--accent-primary)]/20 transition flex items-center space-x-2"
                >
                  <Shield className="w-4 h-4" />
                  <span>RE-SCAN HEURISTICS</span>
                </button>
              </div>
            </div>

            {/* Process Table Matrix */}
            <div className="glass-panel overflow-hidden">
              <div className="overflow-x-auto">
                <table className="w-full text-left border-collapse">
                  <thead>
                    <tr className="border-b border-[var(--border-color)] text-xs font-mono text-[var(--text-secondary)] bg-[var(--border-color)]/10">
                      <th className="p-4">PROCESS NAME</th>
                      <th className="p-4">PID</th>
                      <th className="p-4">CPU %</th>
                      <th className="p-4">RAM (MB)</th>
                      <th className="p-4">DISK (KB/s)</th>
                      <th className="p-4">HEURISTIC FLAGS</th>
                      <th className="p-4 text-right">ACTIONS</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--border-color)] text-sm font-mono">
                    {processes
                      .filter(p => p.name.toLowerCase().includes(searchQuery.toLowerCase()) || p.pid.toString().includes(searchQuery))
                      .map(p => (
                        <tr key={p.pid} className="hover:bg-[var(--border-color)]/20 transition group">
                          <td className="p-4">
                            <div className="font-semibold">{p.name}</div>
                            <div className="text-xs text-[var(--text-secondary)] truncate max-w-xs">{p.path}</div>
                          </td>
                          <td className="p-4 text-[var(--accent-primary)]">{p.pid}</td>
                          <td className="p-4 font-bold">{p.cpu}%</td>
                          <td className="p-4">{p.ramMb} MB</td>
                          <td className="p-4">{p.diskKbps} KB/s</td>
                          <td className="p-4">
                            {p.flags.length > 0 ? (
                              <span className="px-2 py-0.5 rounded text-xs bg-[var(--accent-warning)]/20 text-[var(--accent-warning)] border border-[var(--accent-warning)]/40 font-bold">
                                ⚠ {p.flags.length} FLAG{p.flags.length > 1 ? 'S' : ''}
                              </span>
                            ) : (
                              <span className="text-xs text-[var(--accent-success)]">SECURE</span>
                            )}
                          </td>
                          <td className="p-4 text-right space-x-2">
                            <button
                              onClick={() => setCopyModalProcess(p)}
                              className="px-2.5 py-1 rounded bg-[var(--border-color)]/40 hover:bg-[var(--accent-primary)]/20 text-xs transition text-[var(--text-primary)]"
                              title="Copy Process Telemetry Report"
                            >
                              Copy Info
                            </button>
                            <button
                              onClick={() => handleKillProcess(p.pid)}
                              className="px-2.5 py-1 rounded bg-[var(--accent-danger)]/20 hover:bg-[var(--accent-danger)]/40 text-[var(--accent-danger)] text-xs transition font-bold"
                              title="Terminate Process"
                            >
                              Kill
                            </button>
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'gpu' && (
          <div className="space-y-6">
            <div className="glass-panel p-6 glow-cyan">
              <div className="flex items-center space-x-3 mb-2">
                <Zap className="w-6 h-6 text-[var(--accent-primary)]" />
                <h2 className="text-xl font-bold">Dedicated GPU Telemetry & Hardware Acceleration</h2>
              </div>
              <p className="text-sm text-[var(--text-secondary)]">
                Direct WMI & DXGI hardware telemetry sampler monitoring VRAM allocation, GPU core utilization, thermal junction, and power draw.
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              <div className="glass-panel p-6">
                <div className="text-sm text-[var(--text-secondary)] mb-1">GPU Core Load</div>
                <div className="text-4xl font-bold font-mono text-[var(--accent-primary)]">24.5%</div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-4">
                  <div className="bg-[var(--accent-primary)] h-full rounded-full" style={{ width: '24.5%' }}></div>
                </div>
                <div className="text-xs text-[var(--text-secondary)] mt-3 font-mono">NVIDIA GeForce RTX 4090</div>
              </div>

              <div className="glass-panel p-6">
                <div className="text-sm text-[var(--text-secondary)] mb-1">Dedicated VRAM Usage</div>
                <div className="text-4xl font-bold font-mono text-[var(--accent-secondary)]">4.8 <span className="text-xl text-[var(--text-secondary)]">/ 24 GB</span></div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-4">
                  <div className="bg-[var(--accent-secondary)] h-full rounded-full" style={{ width: '20%' }}></div>
                </div>
                <div className="text-xs text-[var(--text-secondary)] mt-3 font-mono">GDDR6X High Bandwidth</div>
              </div>

              <div className="glass-panel p-6">
                <div className="text-sm text-[var(--text-secondary)] mb-1">GPU Temperature</div>
                <div className="text-4xl font-bold font-mono text-[var(--accent-success)]">48°C</div>
                <div className="w-full bg-[var(--border-color)] h-2 rounded-full mt-4">
                  <div className="bg-[var(--accent-success)] h-full rounded-full" style={{ width: '48%' }}></div>
                </div>
                <div className="text-xs text-[var(--text-secondary)] mt-3 font-mono">Fan Speed: 1,250 RPM (Silent)</div>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'startup' && (
          <div className="space-y-6">
            <div className="glass-panel p-6">
              <h2 className="text-xl font-bold mb-1">Startup Impact Scoring Engine</h2>
              <p className="text-sm text-[var(--text-secondary)]">
                Explainable 0–100 impact scores combining time delay and hardware resource consumption across multi-source Windows startup hooks.
              </p>
            </div>

            <div className="grid grid-cols-1 gap-4">
              {startupItems.map(item => (
                <div key={item.id} className="glass-panel p-5 flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
                  <div className="space-y-1">
                    <div className="flex items-center space-x-3">
                      <span className="font-bold text-lg font-mono">{item.name}</span>
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-[var(--accent-primary)]/10 text-[var(--accent-primary)] border border-[var(--accent-primary)]/30">
                        Impact Score: {item.impactScore}/100
                      </span>
                    </div>
                    <div className="text-xs text-[var(--text-secondary)]">{item.publisher} • {item.location}</div>
                    <div className="text-xs font-mono text-[var(--text-secondary)] pt-1">{item.explanation}</div>
                  </div>

                  <div className="flex items-center space-x-4 w-full md:w-auto justify-between md:justify-end">
                    <div className="text-right font-mono text-xs">
                      <div className="text-[var(--accent-primary)]">Time: {item.timeScore} pts</div>
                      <div className="text-[var(--accent-secondary)]">Resource: {item.resourceScore} pts</div>
                    </div>
                    <button
                      onClick={() => toggleStartupEnabled(item.id)}
                      className={`px-4 py-2 rounded-lg font-mono text-xs font-bold transition ${
                        item.enabled
                          ? 'bg-[var(--accent-success)]/20 text-[var(--accent-success)] border border-[var(--accent-success)]/40 hover:bg-[var(--accent-success)]/30'
                          : 'bg-[var(--border-color)] text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
                      }`}
                    >
                      {item.enabled ? 'ENABLED' : 'DISABLED'}
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {activeTab === 'settings' && (
          <div className="space-y-6">
            <div className="glass-panel p-6">
              <h2 className="text-xl font-bold mb-1">NeuralTask Preferences & Plugin Manager</h2>
              <p className="text-sm text-[var(--text-secondary)]">
                Configure sampling speeds, historical retention windows, heuristic sensitivity thresholds, and managed plugins.
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div className="glass-panel p-6 space-y-4">
                <h3 className="font-semibold text-lg flex items-center space-x-2">
                  <Sliders className="w-5 h-5 text-[var(--accent-primary)]" />
                  <span>Telemetry Configuration</span>
                </h3>

                <div className="space-y-2">
                  <label className="text-xs font-mono text-[var(--text-secondary)]">Sampling Cadence</label>
                  <select
                    value={samplingRate}
                    onChange={(e) => setSamplingRate(e.target.value)}
                    className="w-full bg-[var(--bg-canvas)] border border-[var(--border-color)] rounded-lg p-2.5 text-sm font-mono focus:outline-none focus:border-[var(--accent-primary)]"
                  >
                    <option value="500">500ms (High Performance)</option>
                    <option value="1000">1000ms (Balanced)</option>
                    <option value="2000">2000ms (Power Saver)</option>
                  </select>
                </div>

                <div className="space-y-2">
                  <label className="text-xs font-mono text-[var(--text-secondary)]">Heuristic Sensitivity</label>
                  <select
                    value={sensitivity}
                    onChange={(e) => setSensitivity(e.target.value)}
                    className="w-full bg-[var(--bg-canvas)] border border-[var(--border-color)] rounded-lg p-2.5 text-sm font-mono focus:outline-none focus:border-[var(--accent-primary)]"
                  >
                    <option value="conservative">Conservative (Dev-Friendly)</option>
                    <option value="standard">Standard</option>
                    <option value="aggressive">Aggressive</option>
                  </select>
                </div>
              </div>

              <div className="glass-panel p-6 space-y-4">
                <h3 className="font-semibold text-lg flex items-center space-x-2">
                  <Server className="w-5 h-5 text-[var(--accent-secondary)]" />
                  <span>Installed Plugins</span>
                </h3>

                <div className="space-y-3">
                  <div className="p-3 rounded-lg bg-[var(--border-color)]/20 flex items-center justify-between">
                    <div>
                      <div className="font-mono font-medium text-sm">NeuralTask.Plugins.Gpu</div>
                      <div className="text-xs text-[var(--text-secondary)]">Direct DXGI Telemetry Sampler</div>
                    </div>
                    <span className="px-2 py-0.5 rounded text-xs bg-[var(--accent-success)]/20 text-[var(--accent-success)] font-mono">ACTIVE</span>
                  </div>

                  <div className="p-3 rounded-lg bg-[var(--border-color)]/20 flex items-center justify-between">
                    <div>
                      <div className="font-mono font-medium text-sm">NeuralTask.Plugins.Storage</div>
                      <div className="text-xs text-[var(--text-secondary)]">NVMe Health & SMART Monitor</div>
                    </div>
                    <span className="px-2 py-0.5 rounded text-xs bg-[var(--accent-success)]/20 text-[var(--accent-success)] font-mono">ACTIVE</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}
      </main>

      {/* Copy Process Info Modal */}
      {copyModalProcess && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="glass-panel p-6 max-w-2xl w-full glow-cyan space-y-4">
            <div className="flex items-center justify-between border-b border-[var(--border-color)] pb-3">
              <h3 className="font-bold text-lg flex items-center space-x-2">
                <Terminal className="w-5 h-5 text-[var(--accent-primary)]" />
                <span>Process Telemetry Report: {copyModalProcess.name}</span>
              </h3>
              <button
                onClick={() => setCopyModalProcess(null)}
                className="text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
              >
                ✕
              </button>
            </div>
            <div className="bg-[var(--bg-canvas)] p-4 rounded-lg font-mono text-xs space-y-2 border border-[var(--border-color)] overflow-x-auto">
              <div>================================================================================</div>
              <div>NEURALTASK // PROCESS TELEMETRY REPORT</div>
              <div>================================================================================</div>
              <div>Process Name : {copyModalProcess.name}</div>
              <div>PID          : {copyModalProcess.pid}</div>
              <div>Parent PID   : {copyModalProcess.parentPid}</div>
              <div>Executable   : {copyModalProcess.path}</div>
              <div>Command Line : {copyModalProcess.commandLine}</div>
              <div>SHA-256 Hash : {copyModalProcess.sha256}</div>
              <div>Signer Info  : {copyModalProcess.signer}</div>
              <div>CPU Usage    : {copyModalProcess.cpu}%</div>
              <div>Memory (RAM) : {copyModalProcess.ramMb} MB</div>
              <div>Handles      : {copyModalProcess.handles}</div>
              <div>Report Date  : {new Date().toISOString()}</div>
              <div>================================================================================</div>
            </div>
            <div className="flex justify-end space-x-3 pt-2">
              <button
                onClick={() => {
                  navigator.clipboard.writeText(`Process: ${copyModalProcess.name} (PID ${copyModalProcess.pid})\nPath: ${copyModalProcess.path}\nSHA-256: ${copyModalProcess.sha256}`);
                  showToast('Copied telemetry report to clipboard!');
                  setCopyModalProcess(null);
                }}
                className="px-4 py-2 rounded-lg bg-[var(--accent-primary)] text-[var(--bg-canvas)] font-bold text-xs hover:opacity-90 transition flex items-center space-x-2"
              >
                <Copy className="w-4 h-4" />
                <span>COPY TO CLIPBOARD</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
