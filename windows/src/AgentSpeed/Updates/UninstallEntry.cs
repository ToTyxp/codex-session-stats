using Microsoft.Win32;

namespace AgentSpeed.Updates;

/// <summary>
/// The per-user Apps &amp; Features entry the installer writes. Only its DisplayVersion changes, and only when the key
/// exists and describes the install being updated.
/// </summary>
internal static class UninstallEntry
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AgentSpeed";
    private const string VersionValue = "DisplayVersion";

    /// <summary>Sets DisplayVersion; returns what to restore on rollback, or null when nothing was changed.</summary>
    public static RegistryRestore? TrySetDisplayVersion(string installDirectory, string version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                return null;
            }

            if (key.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location)
                && !SameDirectory(location, installDirectory))
            {
                return null;
            }

            var previous = key.GetValue(VersionValue) as string;
            key.SetValue(VersionValue, version, RegistryValueKind.String);
            return new RegistryRestore(previous);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            App.AppLog.Warn($"Could not update the uninstall entry: {UpdateLog.Describe(ex)}");
            return null;
        }
    }

    public static void Restore(RegistryRestore restore)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (restore.PreviousVersion is null)
            {
                key.DeleteValue(VersionValue, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(VersionValue, restore.PreviousVersion, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            App.AppLog.Warn($"Could not restore the uninstall entry: {UpdateLog.Describe(ex)}");
        }
    }

    private static bool SameDirectory(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a.Trim().Trim('"'))),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

internal sealed record RegistryRestore(string? PreviousVersion);
