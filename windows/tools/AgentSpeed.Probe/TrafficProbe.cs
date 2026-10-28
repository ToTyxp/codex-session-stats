using System.Globalization;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Core.Traffic.Monitor;

namespace AgentSpeed.Probe;

/// <summary>
/// <c>traffic [--seconds N] [--interval S]</c>: runs the traffic monitor headless (METRICS 10) into a temporary store and
/// prints, once per second, this computer's rate, the per-process state and every source's rates and today's bytes; at
/// the end the classified processes, the root agent processes' bytes and the average tick cost.
/// </summary>
internal static class TrafficProbe
{
    public const string Usage = "usage: AgentSpeed.Probe traffic [--seconds N] [--interval 0|1|5]";

    public static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("traffic sampling needs Windows");
            return 2;
        }

        var seconds = 20;
        int? interval = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 3600:
                    seconds = n;
                    i++;
                    break;
                case "--interval" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s is >= 0 and <= 60:
                    interval = s;
                    i++;
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        var store = Path.Combine(Path.GetTempPath(), "agentspeed-probe-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var hub = new TrafficHub(context: null);
        using var monitor = TrafficMonitor.CreateForWindows(hub, store, message => Console.WriteLine("log: " + message));
        hub.SnapshotChanged += (_, _) => Print(hub.Snapshot);
        monitor.Start(true);
        monitor.SetDetailDemand(true);
        if (interval is { } fixedInterval)
        {
            monitor.SetForcedInterval(fixedInterval);
        }

        var self = System.Diagnostics.Process.GetCurrentProcess();
        var cpuStart = self.TotalProcessorTime;
        var wallStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        self.Refresh();
        var cpu = (self.TotalProcessorTime - cpuStart).TotalSeconds / System.Diagnostics.Stopwatch.GetElapsedTime(wallStart).TotalSeconds * 100;
        var diagnostics = monitor.Diagnostics();
        Console.WriteLine($"interfaces: {string.Join(", ", diagnostics.CountedInterfaces)}");
        Console.WriteLine($"table: {diagnostics.TableSize} processes; ticks {diagnostics.Ticks}; first tick {diagnostics.FirstTickMicros:0} µs; " +
            $"average tick {diagnostics.AverageTickMicros:0} µs; last error {diagnostics.LastError ?? "-"}");
        Console.WriteLine($"process cpu {cpu:0.000}% of one core; phases " +
            string.Join(", ", TrafficDiagnostics.PhaseNames.Zip(diagnostics.PhaseMicros, (name, micros) => $"{name} {micros:0} µs")));
        foreach (var process in diagnostics.Processes)
        {
            Console.WriteLine($"  {process.Pid,7} {process.Source,-14} {process.Role,-5} root {process.RootPid,7} {(process.Alive ? "alive" : "exited")} {Path.GetFileName(process.Executable)}");
        }

        foreach (var root in diagnostics.Roots)
        {
            var assigned = string.Join(", ", root.Assigned.Select(a => $"{(a.Key.Length == 0 ? "source only" : Path.GetFileName(a.Key))}={a.Value.Sum}"));
            Console.WriteLine($"  root {root.Pid} {root.Source}: self ↓{root.Self.Rx} ↑{root.Self.Tx} tools ↓{root.Tools.Rx} ↑{root.Tools.Tx} ({assigned})");
        }

        monitor.Dispose();
        try
        {
            HistoryStore.Delete(store);
        }
        catch (IOException)
        {
        }

        return 0;
    }

    private static void Print(TrafficSnapshot snapshot)
    {
        if (!snapshot.Enabled)
        {
            return;
        }

        var machine = snapshot.MachineLive is { } m ? $"↓{TrafficFormat.Rate(m.Rx)} ↑{TrafficFormat.Rate(m.Tx)}" : "-";
        var last = snapshot.MachineHistory.Count > 0 ? snapshot.MachineHistory[^1].Rate : TrafficRate.Zero;
        var line = $"{snapshot.Timestamp.ToLocalTime():HH:mm:ss} machine {machine} (bucket ↓{TrafficFormat.Rate(last.Rx)} ↑{TrafficFormat.Rate(last.Tx)}) " +
            $"process {snapshot.ProcessState.Kind}{(snapshot.ProcessState.Reason is { } r ? "/" + r : string.Empty)} {snapshot.ProcessState.Interval}s";
        foreach (var (source, traffic) in snapshot.Sources)
        {
            var live = traffic.LiveWithTools is { } l ? $"↓{TrafficFormat.Rate(l.Rx)} ↑{TrafficFormat.Rate(l.Tx)}" : "-";
            var today = traffic.Today.Total(true);
            line += $" | {source} {live} today ↓{TrafficFormat.Total(today.Rx)} ↑{TrafficFormat.Total(today.Tx)}";
        }

        Console.WriteLine(line.Replace(TrafficFormat.NoBreakSpace, ' '));
    }
}
