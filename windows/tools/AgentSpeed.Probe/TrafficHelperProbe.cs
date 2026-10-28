using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.Core.Traffic.Windows;

namespace AgentSpeed.Probe;

/// <summary>
/// <c>traffic-helper</c>: drives the real client code paths against the installed helper (WINDOWS-SERVICE 8): the
/// SCM/registry query, start, the verified connection, hello and 1 Hz snapshots, and the elevated install / remove.
/// </summary>
internal static class TrafficHelperProbe
{
    public const string Usage =
        "usage: AgentSpeed.Probe traffic-helper [--seconds N] [--no-start] [--force-connect] | traffic-helper install HELPER.exe | traffic-helper remove HELPER.exe";

    private const int UsageError = 2;
    private const int NotInstalled = 3;
    private const int Outdated = 4;
    private const int Stopped = 5;
    private const int Denied = 6;
    private const int Error = 7;
    private const int WindowsOnly = 8;

    public static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("traffic-helper: Windows only");
            return WindowsOnly;
        }

        if (args is [("install" or "remove") and var verb, var path])
        {
            return Install(verb == "install", path);
        }

        var seconds = 20;
        var start = true;
        var force = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n is > 0 and <= 86_400:
                    seconds = n;
                    i++;
                    break;
                case "--no-start":
                    start = false;
                    break;
                case "--force-connect":
                    // Diagnostics: connect whatever the SCM reports, to check that a foreign pipe server is rejected.
                    start = false;
                    force = true;
                    break;
                default:
                    Console.Error.WriteLine($"unknown or incomplete argument: {args[i]}");
                    Console.Error.WriteLine(Usage);
                    return UsageError;
            }
        }

        using var output = new ProbeOutput();
        return Watch(output, seconds, start, force);
    }

    [SupportedOSPlatform("windows")]
    private static int Install(bool install, string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"cannot read {path}: {ex.Message}");
            return UsageError;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Console.WriteLine($"{(install ? "install" : "remove")}: helper {bytes.Length} bytes sha256 {hash} user {HelperInstaller.CurrentUserSid() ?? "?"}");
        var clock = Stopwatch.StartNew();
        var result = install
            ? HelperInstaller.InstallAsync(bytes, hash, 0).GetAwaiter().GetResult()
            : HelperInstaller.RemoveAsync(bytes, hash, 0).GetAwaiter().GetResult();
        Console.WriteLine($"result={result.Outcome} error={result.Win32Error} ms={clock.ElapsedMilliseconds} message={result.Message}");
        Console.WriteLine(Describe(HelperServiceControl.Query()));
        return result.Outcome switch
        {
            HelperInstallOutcome.Done => 0,
            HelperInstallOutcome.Cancelled => 10,
            HelperInstallOutcome.Refused => 11,
            _ => 12,
        };
    }

    [SupportedOSPlatform("windows")]
    private static int Watch(ProbeOutput output, int seconds, bool start, bool force)
    {
        var info = HelperServiceControl.Query();
        output.Line(Describe(info));
        output.Flush();
        if (!info.IsInstalledForCurrentUser && !force)
        {
            return Result(output, NotInstalled, "notInstalled");
        }

        if (info.IsOutdated && !force)
        {
            return Result(output, Outdated, "outdated");
        }

        if (info.State != HelperServiceState.Running && !force)
        {
            if (!start)
            {
                return Result(output, Stopped, "stopped", "state=" + info.State);
            }

            var error = HelperServiceControl.Start();
            if (error != 0)
            {
                return Result(output, error == 5 ? Denied : Stopped, error == 5 ? "denied" : "stopped", $"start error={error}");
            }

            if (!WaitRunning())
            {
                return Result(output, Stopped, "stopped", "the service did not reach Running within 10 s");
            }

            output.Line(Describe(HelperServiceControl.Query()));
        }

        var clock = Stopwatch.StartNew();
        using var client = new HelperClient(line => Console.Error.WriteLine(line));
        var connected = Connect(client, clock);
        switch (connected)
        {
            case HelperConnectResult.Denied:
                return Result(output, Denied, "denied", $"error={client.LastError}");
            case HelperConnectResult.NotReady:
                return Result(output, Stopped, "stopped", "the pipe did not appear within 5 s");
            case HelperConnectResult.Failed:
                return Result(output, Error, "error", $"error={client.LastError}");
        }

        var call = client.Hello(clock.ElapsedMilliseconds, out var hello, out var epoch);
        if (call != HelperCallResult.Ok)
        {
            return call == HelperCallResult.UnsupportedVersion
                ? Result(output, Outdated, "outdated", $"helper protocol {hello.MinProtocol}-{hello.MaxProtocol}")
                : Result(output, Error, "error", $"hello {call} error={client.LastError}");
        }

        output.Line($"hello: protocol {hello.MinProtocol}-{hello.MaxProtocol} version {hello.Version} flags {hello.Flags} " +
            $"lost {hello.EventsLost} unparsed {hello.UnparsedEvents} evicted {hello.EvictedEntries} epoch {epoch}");
        if (hello.Version < HelperProtocol.MinimumVersion)
        {
            return Result(output, Outdated, "outdated", $"helper {hello.Version} is older than {HelperProtocol.MinimumVersion}");
        }
        output.Flush();
        return Sample(output, client, clock, seconds, epoch);
    }

    [SupportedOSPlatform("windows")]
    private static int Sample(ProbeOutput output, HelperClient client, Stopwatch clock, int seconds, ulong epoch)
    {
        var entries = new List<HelperEntry>();
        var previous = new Dictionary<(uint Pid, ulong Created), HelperEntry>();
        var totals = new Dictionary<(uint Pid, ulong Created), HelperEntry>();
        var deltas = new List<(HelperEntry Entry, ulong Rx, ulong Tx)>();
        for (var second = 1; second <= seconds; second++)
        {
            var due = second * 1000L;
            var wait = due - clock.ElapsedMilliseconds;
            if (wait > 0)
            {
                Thread.Sleep((int)wait);
            }

            var call = client.Snapshot(clock.ElapsedMilliseconds, entries, out var snapshotEpoch);
            if (call == HelperCallResult.RateLimited)
            {
                output.Line($"t={second} rate limited");
                continue;
            }

            if (call != HelperCallResult.Ok)
            {
                WriteTotals(output, totals);
                return Result(output, Error, "error", $"snapshot {call} error={client.LastError}");
            }

            if (snapshotEpoch != epoch)
            {
                output.Line($"t={second} new epoch {snapshotEpoch}");
                epoch = snapshotEpoch;
                previous.Clear();
            }

            deltas.Clear();
            foreach (var entry in entries)
            {
                var key = (entry.Pid, entry.CreateTime);
                var before = previous.TryGetValue(key, out var seen) ? seen : default;
                deltas.Add((entry, entry.Rx >= before.Rx ? entry.Rx - before.Rx : 0, entry.Tx >= before.Tx ? entry.Tx - before.Tx : 0));
                previous[key] = entry;
                totals[key] = entry;
            }

            output.Line($"t={second} entries={entries.Count}");
            foreach (var (entry, rx, tx) in deltas.Where(d => d.Rx + d.Tx > 0).OrderByDescending(d => d.Rx + d.Tx).Take(5))
            {
                output.Line($"  pid={entry.Pid} drx={rx} dtx={tx} rx={entry.Rx} tx={entry.Tx} exited={(entry.Exited ? 1 : 0)}");
            }

            output.Flush();
        }

        WriteTotals(output, totals);
        output.Line("result=ok");
        output.Flush();
        return 0;
    }

    private static void WriteTotals(ProbeOutput output, Dictionary<(uint Pid, ulong Created), HelperEntry> totals)
    {
        foreach (var entry in totals.Values.OrderByDescending(e => e.Rx + e.Tx))
        {
            output.Line($"total pid={entry.Pid} created={entry.CreateTime} rx={entry.Rx} tx={entry.Tx} exited={(entry.Exited ? 1 : 0)}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static HelperConnectResult Connect(HelperClient client, Stopwatch clock)
    {
        var deadline = clock.ElapsedMilliseconds + 5_000;
        while (true)
        {
            var result = client.Connect();
            if (result != HelperConnectResult.NotReady || clock.ElapsedMilliseconds >= deadline)
            {
                return result;
            }

            Thread.Sleep(200);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WaitRunning()
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 10_000)
        {
            if (HelperServiceControl.Query().State == HelperServiceState.Running)
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return false;
    }

    private static string Describe(HelperServiceInfo info) =>
        $"service: exists={info.Exists} state={info.State} pid={info.ProcessId} version={info.Version?.ToString() ?? "-"} " +
        $"protocol={info.Protocol?.ToString() ?? "-"} allowed={info.CurrentUserAllowed} users={info.AllowedUsers.Count} " +
        $"scmError={info.Win32Error} image={info.ImagePath ?? "-"}";

    private static int Result(ProbeOutput output, int code, string result, string? detail = null)
    {
        output.Line(detail is null ? $"result={result}" : $"result={result} {detail}");
        output.Flush();
        return code;
    }
}
