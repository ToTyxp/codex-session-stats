using System.Diagnostics;
using AgentSpeed.Core;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Probe;

/// <summary><c>--dump-history</c>: analyses every log completely and prints one line per day and source (METRICS 8).</summary>
internal static class DumpHistory
{
    public static int Run(ProbeOptions options, ProbeOutput output)
    {
        if (options.PriceTable(out var tableError) is not { } table)
        {
            Console.Error.WriteLine(tableError);
            return 2;
        }

        var zone = options.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        var temporary = options.StorePath is null
            ? Path.Combine(Path.GetTempPath(), "agentspeed-probe-" + Guid.NewGuid().ToString("N"))
            : null;
        var storePath = Path.GetFullPath(options.StorePath ?? Path.Combine(temporary!, "history.sqlite"));
        var process = Process.GetCurrentProcess();
        HistoryReport report;
        List<DailyUsage> days;
        long storeBytes;
        try
        {
            using (var store = HistoryStore.Open(storePath))
            {
                var tuning = new HistoryOptions();
                if (Environment.GetEnvironmentVariable("AGENTSPEED_HISTORY_WORKERS") is { } workers && int.TryParse(workers, out var w))
                {
                    tuning = tuning with { Workers = w };
                }

                if (Environment.GetEnvironmentVariable("AGENTSPEED_HISTORY_BUFFER_MB") is { } buffer && long.TryParse(buffer, out var mb))
                {
                    tuning = tuning with { MaxBufferedBytes = mb * 1024 * 1024 };
                }

                var analyzer = new HistoryAnalyzer(store, tuning, new PriceBook([table]));
                report = analyzer.Run(
                    options.Locations, new HashSet<SourceKind>(Enum.GetValues<SourceKind>()), zone, CancellationToken.None);
                days = analyzer.Days();
            }

            storeBytes = new[] { "", "-wal", "-shm" }
                .Select(suffix => new FileInfo(storePath + suffix))
                .Where(f => f.Exists)
                .Sum(f => f.Length);
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }

        foreach (var day in days)
        {
            var models = new JsonLine();
            foreach (var model in day.Models.OrderBy(m => m.Model, StringComparer.Ordinal))
            {
                models.Raw(model.Model, new JsonLine()
                    .Add("requests", model.Requests)
                    .Add("output", model.Output)
                    .Add("measuredOutput", model.MeasuredOutput)
                    .Add("measuredMs", model.MeasuredMs)
                    .Add("costMicroUsd", model.Cost?.CostMicroUsd)
                    .Add("unpricedRequests", model.Cost?.UnpricedRequests)
                    .Text);
            }

            output.Line(new JsonLine()
                .Add("date", day.Day)
                .Add("source", JsonLine.SourceName(day.Source))
                .Add("requests", day.Totals.Requests)
                .Add("input", day.Totals.Input)
                .Add("cachedInput", day.Totals.CachedInput)
                .Add("cacheWriteInput", day.Totals.CacheWriteInput)
                .Add("output", day.Totals.Output)
                .Add("reasoning", day.Totals.Reasoning)
                .Add("measuredOutput", day.MeasuredOutput)
                .Add("measuredMs", day.MeasuredMs)
                .Add("unfinalized", day.Unfinalized)
                .Add("costMicroUsd", day.Cost?.CostMicroUsd)
                .Add("unpricedRequests", day.Cost?.UnpricedRequests)
                .Add("priceVersion", day.PriceVersion)
                .Add("agents", day.Agents)
                .Raw("models", models.Text)
                .Text);
        }

        output.Flush();
        process.Refresh();
        Console.Error.WriteLine(
            $"history: discovered {report.FilesDiscovered}, analysed {report.FilesAnalyzed} files ({report.BytesAnalyzed} bytes) " +
            $"and {report.SessionsAnalyzed} OpenCode sessions in {report.ElapsedMs} ms; unreadable {report.FilesUnreadable}; " +
            $"store {storeBytes} bytes; working set {process.WorkingSet64 / 1048576.0:F1} MB; " +
            $"allocated {GC.GetTotalAllocatedBytes() / 1048576.0:F0} MB; {days.Count} rows");
        return 0;
    }
}
