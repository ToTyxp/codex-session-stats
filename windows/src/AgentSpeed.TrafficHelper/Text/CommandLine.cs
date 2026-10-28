namespace AgentSpeed.TrafficHelper.Text;

/// <summary>What the process was started to do.</summary>
internal enum HelperVerb
{
    /// <summary>No arguments: run as the service (under the SCM).</summary>
    Service,
    Install,
    Remove,

    /// <summary>Anything else: print usage, exit 1.</summary>
    Usage,
}

/// <summary>The helper's command line (WINDOWS-SERVICE 5): <c>install --user SID --sha256 HEX</c> or <c>remove --user SID</c>.</summary>
internal sealed record CommandLine(HelperVerb Verb, string? UserSid = null, byte[]? Sha256 = null)
{
    public const string Usage =
        "usage: AgentSpeedTrafficHelper install --user <SID> --sha256 <hex>\n" +
        "       AgentSpeedTrafficHelper remove --user <SID>\n" +
        "Without arguments it runs as the AgentSpeedTraffic service (started by the Service Control Manager).\n";

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CommandLine(HelperVerb.Service);
        }

        if (args.Count == 5 && args[0] == "install")
        {
            string? user = null, hash = null;
            for (var i = 1; i < 5; i += 2)
            {
                switch (args[i])
                {
                    case "--user" when user is null:
                        user = args[i + 1];
                        break;
                    case "--sha256" when hash is null:
                        hash = args[i + 1];
                        break;
                    default:
                        return new CommandLine(HelperVerb.Usage);
                }
            }

            return SidText.TryNormalizeUserSid(user, out var sid) && SecurityText.TryParseSha256(hash, out var bytes)
                ? new CommandLine(HelperVerb.Install, sid, bytes)
                : new CommandLine(HelperVerb.Usage);
        }

        if (args.Count == 3 && args[0] == "remove" && args[1] == "--user" && SidText.TryNormalizeUserSid(args[2], out var removed))
        {
            return new CommandLine(HelperVerb.Remove, removed);
        }

        return new CommandLine(HelperVerb.Usage);
    }
}
