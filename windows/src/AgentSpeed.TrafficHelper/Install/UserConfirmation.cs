using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// Confirms that <c>--user</c> is the user who asked for the install (WINDOWS-SERVICE 2, "confirmed against the elevated
/// process's linked token where available"): the elevated token's own user, its linked (filtered) token's user, or the
/// user of the parent process that started the elevation (the app or installer script — the only way to recognise a
/// standard user who elevated with an administrator's credentials). Without any of these the request is refused,
/// except in a session without a split token (TokenElevationTypeDefault: UAC off, the built-in Administrator, an
/// elevated remote shell), where an administrator is acting directly and there is nothing to confirm against.
/// </summary>
internal static unsafe class UserConfirmation
{
    public static bool Confirms(string userSid, out string how)
    {
        var token = Tokens.OpenProcessToken(Kernel32.GetCurrentProcess());
        if (token == 0)
        {
            how = "no process token";
            return false;
        }

        try
        {
            if (Same(Tokens.UserOf(token), userSid))
            {
                how = "elevated token user";
                return true;
            }

            var linked = Tokens.LinkedToken(token);
            if (linked != 0)
            {
                var linkedUser = Tokens.UserOf(linked);
                Kernel32.CloseHandle(linked);
                if (Same(linkedUser, userSid))
                {
                    how = "linked token user";
                    return true;
                }
            }

            if (Same(ParentUser(), userSid))
            {
                how = "parent process user";
                return true;
            }

            if (Tokens.ElevationType(token) == Advapi32.TokenElevationTypeDefault)
            {
                how = "administrator session without a split token";
                return true;
            }

            how = "the user is neither this token's, its linked token's nor the parent process's";
            return false;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    private static bool Same(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The token user of the parent process, checked to be older than this process (no PID reuse).</summary>
    private static string? ParentUser()
    {
        var processes = new ProcessListReader().Read();
        if (processes is null)
        {
            return null;
        }

        var self = Kernel32.GetCurrentProcessId();
        ProcessInfo? me = null;
        foreach (var process in processes)
        {
            if (process.Pid == self)
            {
                me = process;
                break;
            }
        }

        if (me is not { } current || current.ParentPid <= 4)
        {
            return null;
        }

        var parent = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, 0, current.ParentPid);
        if (parent == 0)
        {
            return null;
        }

        try
        {
            long created, exited, kernel, user;
            if (Kernel32.GetProcessTimes(parent, &created, &exited, &kernel, &user) == 0 || (ulong)created >= current.CreateTime)
            {
                return null;
            }

            var token = Tokens.OpenProcessToken(parent);
            if (token == 0)
            {
                return null;
            }

            try
            {
                return Tokens.UserOf(token);
            }
            finally
            {
                Kernel32.CloseHandle(token);
            }
        }
        finally
        {
            Kernel32.CloseHandle(parent);
        }
    }
}
