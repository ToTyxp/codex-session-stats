using System.Diagnostics;
using System.Globalization;
using AgentSpeed.App;

namespace AgentSpeed.Orb.Hero;

/// <summary>
/// Development measurement (<c>--hero-demo</c>): CPU time and managed allocations of the vehicle layer per frame, logged
/// every 5 s while it renders.
/// </summary>
internal sealed class HeroStats
{
    private const int Capacity = 1024;

    private readonly double[] _samples = new double[Capacity];
    private int _count;
    private long _bytes;
    private long _windowStart = Stopwatch.GetTimestamp();
    private int _gen0 = GC.CollectionCount(0);

    public static bool Enabled { get; set; }

    public static long Begin(out long bytes)
    {
        bytes = Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
        return Enabled ? Stopwatch.GetTimestamp() : 0;
    }

    public void End(long started, long bytes, string state)
    {
        if (!Enabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_count < Capacity)
        {
            _samples[_count++] = (now - started) * 1000.0 / Stopwatch.Frequency;
        }

        _bytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
        var seconds = (now - _windowStart) / (double)Stopwatch.Frequency;
        if (seconds < 5 || _count == 0)
        {
            return;
        }

        var sorted = _samples.AsSpan(0, _count).ToArray();
        Array.Sort(sorted);
        var mean = sorted.Average();
        var p95 = sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.95) - 1)];
        AppLog.Info(string.Create(CultureInfo.InvariantCulture,
            $"Hero vehicle [{state}]: {_count} frames in {seconds:F1} s ({_count / seconds:F1} fps), {mean:F3} ms mean, {p95:F3} ms p95, {sorted[^1]:F3} ms max, {_bytes / _count} B allocated per frame, {GC.CollectionCount(0) - _gen0} gen0 GCs."));
        _gen0 = GC.CollectionCount(0);
        _count = 0;
        _bytes = 0;
        _windowStart = now;
    }
}
