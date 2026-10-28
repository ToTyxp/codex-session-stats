using System.ComponentModel;
using System.Diagnostics;
using AgentSpeed.App;
using AgentSpeed.Presentation.Updates;

namespace AgentSpeed.Updates;

/// <summary>
/// Replaces the running executable and starts the new one (UPDATES section 5, Windows): stage the new file next to
/// the target, rename the running exe to <c>.old</c> (allowed while it runs), rename the staged file into place,
/// update DisplayVersion, start the new exe with <c>--after-update &lt;old&gt; --wait-pid &lt;pid&gt;</c>. Any failure
/// after the rename puts the old executable back.
/// </summary>
internal static class ExecutableSwap
{
    public const string OldSuffix = ".old";
    public const string StagedSuffix = ".new";

    /// <summary>A new process that exits this quickly did not start properly (the real one waits for this process).</summary>
    private static readonly TimeSpan StartupProbe = TimeSpan.FromMilliseconds(1500);

    /// <summary>The executable that would be replaced, or null when this process cannot update itself.</summary>
    public static string? TargetPath()
    {
        if (AppPaths.ExecutablePath is not { Length: > 0 } path)
        {
            return null;
        }

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target);
        if (directory is null
            || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(target), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(directory, Path.GetFileNameWithoutExtension(target) + ".dll")))
        {
            return null;
        }

        return target;
    }

    /// <summary>Returns once the new executable runs and this process must exit.</summary>
    /// <exception cref="UpdateException">Nothing was changed, or everything was put back.</exception>
    public static void Apply(string newExecutable, string target, SemanticVersion newVersion, string currentVersion, int processId)
    {
        var directory = Path.GetDirectoryName(target) ?? throw new UpdateException(UpdateFailure.Invalid, "The target has no folder.");
        var staged = target + StagedSuffix;
        Stage(newExecutable, staged);

        var old = FreeOldPath(target);
        try
        {
            Move(target, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateFiles.TryDelete(staged);
            throw new UpdateException(UpdateFailure.Permission, "The running executable could not be renamed.", ex);
        }

        AppLog.Info("Update: the running executable was renamed; moving the new one into place.");
        var moved = false;
        RegistryRestore? registry = null;
        try
        {
            Move(staged, target);
            moved = true;
            registry = UninstallEntry.TrySetDisplayVersion(directory, newVersion.ToString());
            StartAndProbe(target, directory, currentVersion, processId);
        }
        catch (Exception ex)
        {
            // Whatever failed after the rename, the old executable goes back (UPDATES section 5).
            AppLog.Warn($"Update: installing failed after the rename, rolling back: {UpdateLog.Describe(ex)}");
            Rollback(target, old, staged, moved, registry);
            throw ex as UpdateException ?? new UpdateException(
                ex is IOException or UnauthorizedAccessException ? UpdateFailure.Permission : UpdateFailure.Invalid,
                "The new executable could not be installed.", ex);
        }
    }

    private static void Stage(string source, string staged)
    {
        try
        {
            File.Copy(source, staged, overwrite: true);
            if (new FileInfo(staged).Length != new FileInfo(source).Length)
            {
                throw new IOException("The staged copy is incomplete.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateFiles.TryDelete(staged);
            throw new UpdateException(UpdateFailure.Permission, "The install folder is not writable.", ex);
        }
    }

    /// <summary><c>&lt;target&gt;.old</c>, or a unique variant when a leftover one cannot be deleted yet.</summary>
    private static string FreeOldPath(string target)
    {
        var old = target + OldSuffix;
        return UpdateFiles.TryDelete(old) ? old : $"{target}.{Guid.NewGuid().ToString("N")[..8]}{OldSuffix}";
    }

    /// <summary>Renames, retrying briefly while another process (usually an antivirus scan) holds the file.</summary>
    private static void Move(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10 && File.Exists(source)
                                           && !File.Exists(destination))
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void StartAndProbe(string target, string directory, string currentVersion, int processId)
    {
        var info = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = directory };
        foreach (var argument in LaunchArguments.ForRelaunch(currentVersion, processId))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new UpdateException(UpdateFailure.Invalid, "The new version did not start.");
        if (process.WaitForExit(StartupProbe))
        {
            throw new UpdateException(UpdateFailure.Invalid, $"The new version exited right away (code {process.ExitCode}).");
        }

        AppLog.Info($"Update: started the new version (pid {process.Id}).");
    }

    private static void Rollback(string target, string old, string staged, bool moved, RegistryRestore? registry)
    {
        if (registry is not null)
        {
            UninstallEntry.Restore(registry);
        }

        if (moved && !UpdateFiles.TryDelete(target, attempts: 10, delayMs: 100))
        {
            try
            {
                File.Move(target, staged, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error($"Update rollback: the new executable could not be removed; it stays in place. {UpdateLog.Describe(ex)}");
                return;
            }
        }

        try
        {
            Move(old, target);
            AppLog.Info("Update rollback: the previous executable is back in place.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"Update rollback: the previous executable could not be renamed back. {UpdateLog.Describe(ex)}");
        }

        UpdateFiles.TryDelete(staged);
    }
}
