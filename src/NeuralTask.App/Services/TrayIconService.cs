using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NeuralTask.Core.Models;

namespace NeuralTask.App.Services;

/// <summary>
/// Manages the Windows system tray icon, live mini-graph GDI+ rendering,
/// hover telemetry tooltip, and minimize-to-tray lifecycle.
/// </summary>
public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly ISettingsService _settingsService;
    private readonly NotifyIcon _notifyIcon;
    private readonly float[] _history = new float[16];
    private int _historyCount = 0;
    private IntPtr _previousHIcon = IntPtr.Zero;
    private bool _isDisposed = false;

    public event Action? RestoreRequested;
    public event Action? ExitRequested;

    public TrayIconService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _notifyIcon = new NotifyIcon
        {
            Text = "NeuralTask // HUD",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke();
        _notifyIcon.Click += (s, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                RestoreRequested?.Invoke();
            }
        };

        BuildContextMenu();
        _settingsService.SettingsChanged += (_, _) => BuildContextMenu();

        // Initial default icon
        UpdateMiniGraph(0f, "NeuralTask // Initializing...");
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open NeuralTask // HUD", null, (_, _) => RestoreRequested?.Invoke())
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold)
        };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());

        var isCpu = _settingsService.CurrentSettings.TrayIconMetric.Equals("CPU", StringComparison.OrdinalIgnoreCase);

        var cpuItem = new ToolStripMenuItem("Graph Metric: CPU %", null, (_, _) =>
        {
            var cur = _settingsService.CurrentSettings;
            _settingsService.SaveSettings(cur with { TrayIconMetric = "CPU" });
            BuildContextMenu();
        })
        {
            Checked = isCpu
        };

        var ramItem = new ToolStripMenuItem("Graph Metric: RAM %", null, (_, _) =>
        {
            var cur = _settingsService.CurrentSettings;
            _settingsService.SaveSettings(cur with { TrayIconMetric = "RAM" });
            BuildContextMenu();
        })
        {
            Checked = !isCpu
        };

        menu.Items.Add(cpuItem);
        menu.Items.Add(ramItem);
        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit NeuralTask", null, (_, _) => ExitRequested?.Invoke());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    public void UpdateMetrics(IReadOnlyList<ProcessSnapshot> processes, SystemMetricsSnapshot systemMetrics)
    {
        if (_isDisposed) return;

        var isCpu = _settingsService.CurrentSettings.TrayIconMetric.Equals("CPU", StringComparison.OrdinalIgnoreCase);
        float currentMetric = (float)(isCpu ? systemMetrics.CpuPercent : systemMetrics.MemoryPercent);

        // 1. Shift and record history
        if (_historyCount < _history.Length)
        {
            _history[_historyCount++] = currentMetric;
        }
        else
        {
            Array.Copy(_history, 1, _history, 0, _history.Length - 1);
            _history[^1] = currentMetric;
        }

        // 2. Format compact hover tooltip (Windows NotifyIcon text limit: 127 chars)
        var topOffenders = processes
            .OrderByDescending(p => p.CpuPercent)
            .Take(2)
            .Select(p => $"{p.Name} ({p.CpuPercent:0.0}%)")
            .ToList();

        var topStr = topOffenders.Count > 0 ? string.Join(", ", topOffenders) : "Idle";
        var metricLabel = isCpu ? "CPU" : "RAM";
        var tooltip = $"NeuralTask [{metricLabel}: {currentMetric:0.0}%]\nRAM: {systemMetrics.UsedMemoryBytes / (1024.0 * 1024 * 1024):0.0} GB\nTop: {topStr}";

        if (tooltip.Length > 126)
        {
            tooltip = tooltip.Substring(0, 123) + "...";
        }

        UpdateMiniGraph(currentMetric, tooltip);
    }

    private void UpdateMiniGraph(float latestValue, string tooltipText)
    {
        const int size = 16;
        var isCpu = _settingsService.CurrentSettings.TrayIconMetric.Equals("CPU", StringComparison.OrdinalIgnoreCase);

        // Sub-millisecond GDI+ bitmap creation
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;

            bool isLight = NeuralTask.Theming.ThemeManager.CurrentTheme == NeuralTask.Theming.ThemeMode.Light;

            // Theme-adaptive background & border
            var bgColor = isLight
                ? System.Drawing.Color.FromArgb(255, 241, 245, 249)
                : System.Drawing.Color.FromArgb(255, 10, 14, 20);

            var borderColor = isLight
                ? System.Drawing.Color.FromArgb(200, 203, 213, 225)
                : System.Drawing.Color.FromArgb(120, 0, 229, 255);

            using var bgBrush = new SolidBrush(bgColor);
            g.FillRectangle(bgBrush, 0, 0, size, size);

            using var borderPen = new System.Drawing.Pen(borderColor, 1);
            g.DrawRectangle(borderPen, 0, 0, size - 1, size - 1);

            // Theme-adaptive metric colors:
            // Light: Deep Teal (#00758A) for CPU, Royal Violet (#6D28D9) for RAM
            // Dark: Electric Cyan (#00E5FF) for CPU, Neon Magenta (#B14EFF) for RAM
            var barColor = isCpu
                ? (isLight ? System.Drawing.Color.FromArgb(255, 0, 117, 138) : System.Drawing.Color.FromArgb(255, 0, 229, 255))
                : (isLight ? System.Drawing.Color.FromArgb(255, 109, 40, 217) : System.Drawing.Color.FromArgb(255, 177, 78, 255));

            using var barPen = new System.Drawing.Pen(barColor, 1);

            int startX = size - 1 - _historyCount;
            for (int i = 0; i < _historyCount; i++)
            {
                int x = startX + i;
                if (x < 1 || x >= size - 1) continue;

                float val = Math.Clamp(_history[i], 0f, 100f);
                int barHeight = (int)Math.Round((val / 100f) * (size - 3));
                if (barHeight < 1 && val > 0) barHeight = 1;

                int yTop = (size - 2) - barHeight;
                g.DrawLine(barPen, x, size - 2, x, yTop);
            }
        }

        // Convert GDI+ Bitmap to Native Icon Handle and clean up previous
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var newIcon = System.Drawing.Icon.FromHandle(hIcon);
            _notifyIcon.Icon = (System.Drawing.Icon)newIcon.Clone();
            _notifyIcon.Text = tooltipText;
        }
        finally
        {
            if (_previousHIcon != IntPtr.Zero)
            {
                DestroyIcon(_previousHIcon);
            }
            _previousHIcon = hIcon;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_previousHIcon != IntPtr.Zero)
        {
            DestroyIcon(_previousHIcon);
            _previousHIcon = IntPtr.Zero;
        }
    }
}
