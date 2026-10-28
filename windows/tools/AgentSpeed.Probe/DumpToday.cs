using AgentSpeed.Core;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Probe;

/// <summary><c>--dump-today</c>: seeds from the real roots and prints every response that ended today.</summary>
internal static class DumpToday
{
    /// <summary>Costs use one table for every response, like <c>reference.py --dump-today</c> (no day bindings).</summary>
    public static int Run(ProbeOptions options, ProbeOutput output)
    {
        if (options.PriceTable(out var tableError) is not { } table)
        {
            Console.Error.WriteLine(tableError);
            return 2;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todayStart = options.SinceMs ?? LocalDay.StartMs(now, TimeZoneInfo.Local);
        var collector = new Collector();
        using var core = new MonitorCore(MonitorConfiguration.Default, options.Locations, todayStart, collector);
        var report = core.Seed(now, todayStart, CancellationToken.None);
        var (snapshot, _) = core.BuildSnapshot(now, todayStart, MonitorState.Running, observeLive: false);

        var today = collector.Latest.Values
            .Where(r => r.EndMs >= todayStart)
            .OrderBy(r => r.EndMs)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
        long costMicroUsd = 0;
        var unpriced = 0L;
        foreach (var r in today)
        {
            var cost = table.Cost(r);
            if (cost is { } known)
            {
                costMicroUsd += known;
            }
            else
            {
                unpriced += r.Requests;
            }

            var row = new JsonLine()
                .Add("key", r.Key)
                .Add("requestCount", r.Requests)
                .Add("source", JsonLine.SourceName(r.Source))
                .Add("agentId", r.AgentId)
                .Add("model", r.Model)
                .Add("startMs", r.StartMs)
                .Add("endMs", r.EndMs)
                .Add("input", r.Tokens.Input)
                .Add("cachedInput", r.Tokens.CachedInput)
                .Add("cacheWriteInput", r.Tokens.CacheWriteInput)
                .Add("cacheWrite1hInput", r.Tokens.CacheWrite1hInput)
                .Add("output", r.Tokens.Output)
                .Add("reasoning", r.Tokens.Reasoning)
                .Add("final", r.Final)
                .Add("provider", r.Provider)
                .Add("fast", r.Fast)
                .Add("costMicroUsd", cost);
            if (!r.Modes.IsEmpty) row.Raw("modes", JsonLine.Modes(r.Modes));
            output.Line(row.Text);
        }

        var counts = new JsonLine();
        foreach (var kind in Enum.GetValues<SourceKind>())
        {
            counts.Add(JsonLine.SourceName(kind), today.Count(r => r.Source == kind));
        }

        var summary = new JsonLine()
            .Raw("counts", counts.Text)
            .Add("output", today.Sum(r => r.Tokens.Output))
            .Add("costMicroUsd", costMicroUsd)
            .Add("unpricedRequests", unpriced)
            .Add("input", today.Sum(r => r.Tokens.Input))
            .Add("filesRead", report.FilesRead)
            .Add("bytesRead", report.BytesRead)
            .Add("seedMs", report.ElapsedMs)
            .Raw("today", JsonLine.Totals(snapshot.Today))
            .Add("filesDiscovered", report.FilesDiscovered)
            .Add("malformedLines", core.MalformedLineCount)
            .Add("unreadableFiles", core.UnreadableFileCount)
            .Add("partialHistory", core.PartialHistory)
            .Add("unfinalized", today.Count(r => !r.Final));
        output.Line(new JsonLine().Raw("summary", summary.Text).Text);
        output.Flush();

        Console.Error.WriteLine(
            $"seeded {report.FilesRead} files, {report.BytesRead} bytes in {report.ElapsedMs} ms; " +
            $"oversized lines {core.OversizedLineCount}; average today {snapshot.AverageToday:F1} tok/s; " +
            $"peak today {snapshot.PeakToday.Output:F1} tok/s; " +
            $"Antigravity connections {core.OpenDatabaseConnections} open, {core.ImmutableDatabaseConnections} immutable");
        return 0;
    }

    private sealed class Collector : IResponseObserver
    {
        public Dictionary<string, ParsedResponse> Latest { get; } = new(StringComparer.Ordinal);

        public void OnResponse(ParsedResponse response, bool isCompletion, bool seeded) => Latest[response.Key] = response;
    }
}
