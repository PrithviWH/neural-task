using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeuralTask.Core.Services;

namespace NeuralTask.Harness;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   NEURALTASK TELEMETRY HARNESS // CORE ENGINE LIVE BENCHMARK");
        Console.WriteLine("===============================================================================");
        Console.WriteLine($"Host Processors: {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"Operating System: {Environment.OSVersion}");
        Console.WriteLine("Initializing ProcessMonitorService (sampling interval: 1000ms)...");
        Console.WriteLine();

        int maxIterations = -1;
        if (args.Length > 0 && int.TryParse(args[0], out var parsedMax))
        {
            maxIterations = parsedMax;
        }

        using var monitor = new ProcessMonitorService();
        int iteration = 0;
        var tcs = new TaskCompletionSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };

        monitor.SnapshotsUpdated += (_, snapshots) =>
        {
            iteration++;
            var sw = Stopwatch.StartNew();
            var tree = ProcessTreeBuilder.BuildTree(snapshots);
            sw.Stop();

            var totalCount = snapshots.Count;
            var accessDeniedCount = snapshots.Count(s => s.IsAccessDenied);
            var orphans = tree.RootNodes.Where(r => r.IsOrphaned).ToList();

            Console.WriteLine($"\n--- [SNAPSHOT #{iteration} @ {DateTime.Now:HH:mm:ss}] ---");
            Console.WriteLine($"Total: {totalCount} | Access Denied: {accessDeniedCount} | Orphaned: {orphans.Count} | Tree Build: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('-', 85));
            Console.WriteLine(string.Format("{0,-7} {1,-26} {2,8} {3,12} {4,14} {5,14}", "PID", "NAME", "CPU %", "RAM (MB)", "DISK (KB/s)", "NET (KB/s)"));
            Console.WriteLine(new string('-', 85));

            // Top processes by CPU
            var topCpu = snapshots
                .OrderByDescending(s => s.CpuPercent)
                .ThenByDescending(s => s.RamBytes)
                .Take(10);

            foreach (var p in topCpu)
            {
                var ramMb = (p.RamBytes / (1024.0 * 1024.0)).ToString("F1");
                var diskKb = (p.DiskBytesPerSec / 1024.0).ToString("F1");
                var netKb = (p.NetworkBytesPerSec / 1024.0).ToString("F1");
                var name = p.Name.Length > 25 ? p.Name.Substring(0, 22) + "..." : p.Name;

                Console.WriteLine(string.Format("{0,-7} {1,-26} {2,8:F2} {3,12} {4,14} {5,14}",
                    p.Pid, name, p.CpuPercent, ramMb, diskKb, netKb));
            }

            if (orphans.Count > 0)
            {
                Console.WriteLine("\n[Orphaned Processes Sample (Parent exited / PID recycled)]:");
                foreach (var orphan in orphans.Take(3))
                {
                    Console.WriteLine($"  - PID {orphan.Process.Pid} ({orphan.Process.Name}) -> Parent PID {orphan.Process.ParentPid} (not running or recycled)");
                }
            }

            if (maxIterations > 0 && iteration >= maxIterations)
            {
                tcs.TrySetResult();
            }
        };

        await monitor.StartAsync();

        if (maxIterations > 0)
        {
            await tcs.Task;
        }
        else
        {
            Console.WriteLine("Press Ctrl+C to terminate harness.");
            await tcs.Task;
        }

        await monitor.StopAsync();
        Console.WriteLine("\nMonitoring stopped cleanly.");
    }
}
