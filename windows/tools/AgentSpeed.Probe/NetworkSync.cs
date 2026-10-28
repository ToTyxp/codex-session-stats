using System.Diagnostics;
using System.Globalization;
using AgentSpeed.Core.Money;
using AgentSpeed.Core.Network;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Probe;

/// <summary>
/// One real price-table round or one exchange-rate round (NETWORK 3, 4), for diagnostics: each request's host, status,
/// size, time and ETag, then what the round concluded. Files go to the given folder, never to the app's own store.
/// </summary>
internal static class NetworkSync
{
    private static readonly string UserAgent = HttpFetchRequest.UserAgentFor("0.0.0");

    /// <summary><c>--sync-prices DIR</c>.</summary>
    public static int Prices(ProbeOptions options, ProbeOutput output)
    {
        var directory = options.SyncDirectory!;
        using var http = new HttpClientTransport();
        var transport = new RecordingTransport(http);
        var log = new List<string>();
        var store = new PriceTableStore(directory, log.Add);
        var sync = new PriceTableSync(store, transport, UserAgent, log: log.Add);
        var bundled = PriceBook.Bundled.Version;
        var clock = Stopwatch.StartNew();
        var result = sync.RunAsync(bundled).GetAwaiter().GetResult();
        output.Line($"price table (bundled v{bundled}):");
        Write(output, transport.Lines, log);
        output.Line($"  outcome: {Describe(result.Outcome)} in {clock.ElapsedMilliseconds} ms");
        if (result.State.Version is { } version)
        {
            output.Line($"  downloaded table: v{version}, updated {result.State.Updated ?? "?"}, from {result.State.Url ?? "?"}");
        }

        if ((result.Table ?? store.LoadTable()) is { } table)
        {
            output.Line($"  stored copy: v{table.Version}, {table.Models.Count} models, {table.Data.Length} bytes");
        }

        output.Flush();
        return result.Outcome is PriceSyncOutcome.Failed ? 1 : 0;
    }

    /// <summary><c>--sync-rates DIR CURRENCY</c>.</summary>
    public static int Rates(ProbeOptions options, ProbeOutput output)
    {
        var currency = options.SyncCurrency!.ToUpperInvariant();
        if (!CurrencyTable.Bundled.Codes.Contains(currency))
        {
            Console.Error.WriteLine($"unsupported currency: {options.SyncCurrency}");
            return 2;
        }

        using var http = new HttpClientTransport();
        var transport = new RecordingTransport(http);
        var log = new List<string>();
        var sync = new ExchangeRateSync(new ExchangeRateStore(options.SyncDirectory!, log.Add), transport, UserAgent, log: log.Add);
        var clock = Stopwatch.StartNew();
        var outcome = sync.RunAsync(currency).GetAwaiter().GetResult();
        output.Line($"exchange rates (selected {currency}):");
        Write(output, transport.Lines, log);
        switch (outcome)
        {
            case ExchangeRateOutcome.Updated { Set: var set }:
                output.Line("  accepted " + Summary(set, currency));
                break;
            case ExchangeRateOutcome.Unchanged { Set: var set }:
                output.Line("  confirmed by 304 " + Summary(set, currency));
                break;
            case ExchangeRateOutcome.Failed failed:
                output.Line($"  failed: {failed.Reason}");
                break;
        }

        output.Line($"  round took {clock.ElapsedMilliseconds} ms");
        output.Flush();
        return outcome is ExchangeRateOutcome.Failed ? 1 : 0;
    }

    private static string Summary(ExchangeRateSet set, string currency)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(set.DateMs).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var rate = set.Rates.TryGetValue(currency, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "missing";
        var status = set.Rates.TryGetValue(currency, out var shown)
            ? $" (status: US$1 = {MoneyFormat.WithSymbol(CurrencyTable.Bundled.CurrencyOrUsd(currency).SymbolFor("zh-Hant"), MoneyFormat.Significant(shown))})"
            : string.Empty;
        return $"from {set.Provider}: {set.Rates.Count} currencies, dated {date}, 1 USD = {rate} {currency}{status}"
            + (set.NeedsAttribution ? " (attribution required)" : string.Empty);
    }

    private static string Describe(PriceSyncOutcome outcome) => outcome switch
    {
        PriceSyncOutcome.UpToDate => "up to date",
        PriceSyncOutcome.Updated updated => $"updated to v{updated.Version}",
        PriceSyncOutcome.NewerSchema => "only a newer schema is offered",
        PriceSyncOutcome.Failed failed => $"failed ({failed.Reason})",
        _ => outcome.ToString(),
    };

    private static void Write(ProbeOutput output, IEnumerable<string> requests, IEnumerable<string> log)
    {
        foreach (var line in requests)
        {
            output.Line(line);
        }

        foreach (var line in log)
        {
            output.Line("  log: " + line);
        }
    }

    /// <summary><see cref="HttpClientTransport"/> with a line per request.</summary>
    private sealed class RecordingTransport(IHttpTransport inner) : IHttpTransport
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public async Task<HttpFetchResponse> GetAsync(HttpFetchRequest request, CancellationToken cancellationToken = default)
        {
            var clock = Stopwatch.StartNew();
            var conditional = request.IfNoneMatch is null ? string.Empty : " (If-None-Match)";
            try
            {
                var response = await inner.GetAsync(request, cancellationToken).ConfigureAwait(false);
                var etag = response.ETag is null ? string.Empty : ", ETag " + response.ETag;
                Add($"  GET {request.Url.AbsoluteUri}{conditional} → {response.Status}, {response.Body.Length} bytes, "
                    + $"{clock.ElapsedMilliseconds} ms{etag} [{request.Url.IdnHost}]");
                return response;
            }
            catch (HttpFetchException ex)
            {
                Add($"  GET {request.Url.AbsoluteUri}{conditional} → failed: {ex.Reason}, {clock.ElapsedMilliseconds} ms [{request.Url.IdnHost}]");
                throw;
            }
        }

        private void Add(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }
    }
}
