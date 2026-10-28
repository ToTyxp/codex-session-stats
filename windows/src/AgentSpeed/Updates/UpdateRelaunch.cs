using System.ComponentModel;
using System.Diagnostics;
using AgentSpeed.App;

namespace AgentSpeed.Updates;

/// <summary>The new process's side of an install (UPDATES section 5): wait for the old one, then clean up after it.</summary>
internal static class UpdateRelaunch
{
    /// <summary>How long to wait for the previous process to exit before taking the single-instance mutex.</summary>
    public static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

    /// <summary>Extra time to wait for the mutex itself, so the second-instance path does not swallow this launch.</summary>
    public static readonly TimeSpan MutexWait = TimeSpan.FromSeconds(10);

    /// <summary>Blocks until <paramref name="processId"/> exits (at most <see cref="ExitWait"/>); true when it is gone.</summary>
    public static bool WaitForExit(int processId)
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            if (processId == current.Id)
            {
                return true;
            }

            using var previous = Process.GetProcessById(processId);
            // A reused process id belongs to an unrelated process; do not wait for it.
            if (!string.Equals(previous.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase)
                || previous.StartTime > current.StartTime)
            {
                return true;
            }

            return previous.WaitForExit(ExitWait);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            AppLog.Warn($"Could not wait for the previous instance: {UpdateLog.Describe(ex)}");
            return false;
        }
    }

    /// <summary>Removes <c>AgentSpeed.exe.old</c> (and leftovers of interrupted installs) next to the running exe, best effort.</summary>
    public static void CleanupLeftovers()
    {
        if (ExecutableSwap.TargetPath() is not { } target || Path.GetDirectoryName(target) is not { } directory)
        {
            return;
        }

        var candidates = new List<string> { target + ExecutableSwap.OldSuffix, target + ExecutableSwap.StagedSuffix };
        try
        {
            candidates.AddRange(Directory.EnumerateFiles(directory, Path.GetFileName(target) + ".*" + ExecutableSwap.OldSuffix));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (UpdateFiles.TryDelete(path, attempts: 20, delayMs: 250))
            {
                AppLog.Info($"Removed {Path.GetFileName(path)} left by an update.");
            }
            else
            {
                AppLog.Warn($"Could not remove {Path.GetFileName(path)} left by an update; will retry at the next start.");
            }
        }
    }
}
