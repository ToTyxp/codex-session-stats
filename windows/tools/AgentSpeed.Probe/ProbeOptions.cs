using AgentSpeed.Core;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Probe;

internal enum ProbeMode
{
    DumpToday,
    DumpHistory,
    Watch,
    SyncPrices,
    SyncRates,
}

/// <param name="GeminiRoots">Gemini CLI <c>tmp</c> folders; empty uses the defaults.</param>
/// <param name="OmpRoots">omp <c>sessions</c> folders; empty uses the default root and every named profile.</param>
/// <param name="AntigravityRoots">Antigravity store (or <c>conversations</c>) folders; empty uses the three default stores.</param>
/// <param name="StorePath">History store to use and keep; null analyses into a temporary store.</param>
/// <param name="SinceMs">Overrides the start of "today" for <c>--dump-today</c> (Unix milliseconds).</param>
/// <param name="PricesPath"><c>--prices PATH</c>: price with this table instead of the bundled one (METRICS 9), as <c>reference.py --prices</c>.</param>
/// <param name="SyncDirectory">Scratch app-data folder of <c>--sync-prices</c> / <c>--sync-rates</c> (NETWORK 3, 4).</param>
/// <param name="SyncCurrency">The selected currency of <c>--sync-rates</c>.</param>
/// <param name="WorkBuddyRoot">The WorkBuddy data folder or its <c>projects</c> folder; null uses <c>~/.workbuddy</c>.</param>
/// <param name="WorkBuddyAiRoot">The WorkBuddy AI data folder or its <c>projects</c> folder; null uses <c>~/.workbuddy-ai</c>.</param>
internal sealed record ProbeOptions(
    ProbeMode Mode,
    int WatchSeconds,
    string? CodexRoot,
    string? ClaudeRoot,
    string? DshRoot,
    string? OpenCodeRoot,
    IReadOnlyList<string> GeminiRoots,
    bool Utc,
    string? StorePath,
    long? SinceMs = null,
    IReadOnlyList<string>? OmpRoots = null,
    IReadOnlyList<string>? AntigravityRoots = null,
    string? PricesPath = null,
    string? SyncDirectory = null,
    string? SyncCurrency = null,
    string? WorkBuddyRoot = null,
    string? WorkBuddyAiRoot = null,
    string? ZCodeRoot = null,
    string? KimiRoot = null,
    string? GrokBuildRoot = null)
{
    public const string Usage =
        "usage: AgentSpeed.Probe (--dump-today [--since UNIXMS] | --dump-history [--utc] [--store PATH] | --watch N | --sync-prices DIR | --sync-rates DIR CURRENCY) [--prices PATH] " +
        "[--codex PATH] [--claude PATH] [--dsh PATH] [--opencode PATH] [--gemini PATH]... [--omp PATH]... [--antigravity PATH]... " +
        "[--workbuddy PATH] [--workbuddy-ai PATH] [--zcode PATH] [--kimi PATH] [--grok-build PATH]";

    public MonitorConfiguration Configuration => new(
        new HashSet<SourceKind>(Enum.GetValues<SourceKind>()),
        CodexRoot,
        ClaudeRoot,
        DshRoot,
        OpenCodeRoot,
        GeminiRoots.Count > 0 ? GeminiRoots[0] : null,
        OmpRoots is { Count: > 0 } omp ? omp[0] : null,
        AntigravityRoots is { Count: > 0 } antigravity ? antigravity[0] : null,
        WorkBuddyRoot,
        WorkBuddyAiRoot, ZCodeRoot, KimiRoot, GrokBuildRoot);

    /// <summary>Resolved roots; several <c>--gemini</c>, <c>--omp</c> or <c>--antigravity</c> folders are all read (like the reference).</summary>
    public SourceLocations Locations
    {
        get
        {
            var resolved = SourceLocations.Resolve(Configuration);
            if (GeminiRoots.Count > 1)
            {
                resolved = resolved with { GeminiRoots = [.. GeminiRoots.Select(SourceLocations.Normalize)] };
            }

            if (OmpRoots is { Count: > 1 } omp)
            {
                resolved = resolved with { OmpRoots = SourceLocations.NormalizeAll(omp) };
            }

            if (AntigravityRoots is { Count: > 1 } antigravity)
            {
                resolved = resolved with
                {
                    AntigravityStores = SourceLocations.NormalizeAll(antigravity.SelectMany(LiveMonitor.AntigravityStores)),
                };
            }

            return resolved;
        }
    }

    /// <summary>The table of <c>--prices</c>, else the bundled one; null with <paramref name="error"/> when the file does not validate.</summary>
    public PriceTable? PriceTable(out string? error)
    {
        error = null;
        if (PricesPath is null)
        {
            return PriceBook.Bundled;
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(PricesPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"cannot read {PricesPath}";
            return null;
        }

        var loaded = AgentSpeed.Core.Pricing.PriceTable.Load(data);
        error = loaded.Table is null ? $"{PricesPath} is not a valid price table: {loaded.Error}" : null;
        return loaded.Table;
    }

    public static ProbeOptions? Parse(string[] args, out string? error)
    {
        error = null;
        ProbeMode? mode = null;
        var seconds = 0;
        var utc = false;
        string? codex = null, claude = null, dsh = null, opencode = null, store = null, prices = null, sync = null, currency = null;
        string? workBuddy = null, workBuddyAi = null, zcode = null, kimi = null, grokBuild = null;
        long? since = null;
        var gemini = new List<string>();
        var omp = new List<string>();
        var antigravity = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump-today":
                    mode = ProbeMode.DumpToday;
                    break;
                case "--dump-history":
                    mode = ProbeMode.DumpHistory;
                    break;
                case "--utc":
                    utc = true;
                    break;
                case "--watch" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
                    mode = ProbeMode.Watch;
                    seconds = n;
                    i++;
                    break;
                case "--codex" when i + 1 < args.Length:
                    codex = args[++i];
                    break;
                case "--claude" when i + 1 < args.Length:
                    claude = args[++i];
                    break;
                case "--dsh" when i + 1 < args.Length:
                    dsh = args[++i];
                    break;
                case "--opencode" when i + 1 < args.Length:
                    opencode = args[++i];
                    break;
                case "--gemini" when i + 1 < args.Length:
                    gemini.Add(args[++i]);
                    break;
                case "--omp" when i + 1 < args.Length:
                    omp.Add(args[++i]);
                    break;
                case "--antigravity" when i + 1 < args.Length:
                    antigravity.Add(args[++i]);
                    break;
                case "--workbuddy" when i + 1 < args.Length:
                    workBuddy = args[++i];
                    break;
                case "--workbuddy-ai" when i + 1 < args.Length:
                    workBuddyAi = args[++i];
                    break;
                case "--zcode" when i + 1 < args.Length:
                    zcode = args[++i];
                    break;
                case "--kimi" when i + 1 < args.Length:
                    kimi = args[++i];
                    break;
                case "--grok-build" when i + 1 < args.Length:
                    grokBuild = args[++i];
                    break;
                case "--store" when i + 1 < args.Length:
                    store = args[++i];
                    break;
                case "--prices" when i + 1 < args.Length:
                    prices = args[++i];
                    break;
                case "--sync-prices" when i + 1 < args.Length:
                    mode = ProbeMode.SyncPrices;
                    sync = args[++i];
                    break;
                case "--sync-rates" when i + 2 < args.Length:
                    mode = ProbeMode.SyncRates;
                    sync = args[++i];
                    currency = args[++i];
                    break;
                case "--since" when i + 1 < args.Length && long.TryParse(args[i + 1], out var ms):
                    since = ms;
                    i++;
                    break;
                default:
                    error = $"unknown or incomplete argument: {args[i]}";
                    return null;
            }
        }

        if (mode is null)
        {
            error = "missing --dump-today, --dump-history, --watch N, --sync-prices DIR or --sync-rates DIR CURRENCY";
            return null;
        }

        return new ProbeOptions(
            mode.Value, seconds, codex, claude, dsh, opencode, gemini, utc, store, since, omp, antigravity, prices, sync, currency,
            workBuddy, workBuddyAi, zcode, kimi, grokBuild);
    }
}
