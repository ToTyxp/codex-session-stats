using System.Diagnostics;
using AgentSpeed.Core;

namespace AgentSpeed.Probe;

/// <summary><c>--watch N</c>: runs <see cref="LiveMonitor"/> and prints one compact snapshot per second.</summary>
internal static class Watch
{
    public static int Run(ProbeOptions options, ProbeOutput output)
    {
        var hub = new SpeedHub(context: null);
        var latest = SpeedSnapshot.Empty;
        int responses = 0, peaks = 0;
        hub.SnapshotChanged += (_, snapshot) => Volatile.Write(ref latest, snapshot);
        hub.EventRaised += (_, speedEvent) =>
        {
            if (speedEvent is SpeedEvent.NewPeak)
            {
                Interlocked.Increment(ref peaks);
            }
            else
            {
                Interlocked.Increment(ref responses);
            }
        };

        var configuration = options.Configuration;
        var process = Process.GetCurrentProcess();
        var started = Stopwatch.StartNew();
        TimeSpan? runningCpu = null;
        var runningWall = TimeSpan.Zero;
        using (var monitor = new LiveMonitor(hub, configuration))
        {
            monitor.Start();
            for (var second = 1; second <= options.WatchSeconds; second++)
            {
                var wait = TimeSpan.FromSeconds(second) - started.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }

                var snapshot = Volatile.Read(ref latest);
                if (runningCpu is null && snapshot.State.Kind == MonitorStateKind.Running)
                {
                    process.Refresh();
                    runningCpu = process.TotalProcessorTime;
                    runningWall = started.Elapsed;
                }

                output.Line(Line(snapshot));
                output.Flush();
            }
        }

        process.Refresh();
        var steadyCpu = runningCpu is { } cpu ? process.TotalProcessorTime - cpu : TimeSpan.Zero;
        var steadyWall = started.Elapsed - runningWall;
        Console.Error.WriteLine(
            $"cpu {process.TotalProcessorTime.TotalMilliseconds:F0} ms total, {steadyCpu.TotalMilliseconds:F0} ms over " +
            $"{steadyWall.TotalSeconds:F1} s while running ({100 * steadyCpu / steadyWall:F2}% of one core); " +
            $"working set {process.WorkingSet64 / 1048576.0:F1} MB, live heap {GC.GetTotalMemory(true) / 1048576.0:F1} MB; " +
            $"events: {responses} responses, {peaks} peaks");
        return 0;
    }

    private static string Line(SpeedSnapshot s)
    {
        var line = new JsonLine().Add("t", s.Timestamp == DateTimeOffset.MinValue ? 0 : s.Timestamp.ToUnixTimeMilliseconds());
        switch (s.State.Kind)
        {
            case MonitorStateKind.Starting:
                line.Add("state", "starting").Add("progress", s.State.Progress);
                break;
            case MonitorStateKind.Running:
                line.Add("state", "running");
                break;
            default:
                line.Add("state", "noSources");
                break;
        }

        var agents = s.Agents.Select(a => new JsonLine()
            .Add("id", Path.GetFileName(a.Id))
            .Add("source", JsonLine.SourceName(a.Source))
            .Add("phase", JsonLine.PhaseName(a.Phase))
            .Add("live", a.Live.Output)
            .Add("parent", a.ParentId is null ? null : Path.GetFileName(a.ParentId))
            .Text);
        return line
            .Raw("live", JsonLine.Rate(s.Live))
            .Add("estimatedShare", s.LiveEstimatedShare)
            .Add("generating", s.GeneratingCount)
            .Add("tools", s.ToolsCount)
            .Raw("agents", JsonLine.Array(agents))
            .Raw("today", JsonLine.Totals(s.Today))
            .Raw("peak", JsonLine.Rate(s.PeakToday))
            .Text;
    }
}
