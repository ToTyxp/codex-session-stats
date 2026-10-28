using Microsoft.Win32;

namespace AgentSpeed.App;

/// <summary>Launch at login through HKCU\...\Run (same value the installer writes).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AgentSpeed";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var command = Command();
                if (command is null)
                {
                    return false;
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Could not update the Run key.", ex);
            return false;
        }
    }

    /// <summary>Keeps an existing entry pointing at the running executable (after an update or a move).</summary>
    public static void Refresh()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string current || Command() is not { } command)
            {
                return;
            }

            if (!string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Could not refresh the Run key.", ex);
        }
    }

    private static string? Command()
    {
        var path = AppPaths.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"', StringComparison.Ordinal))
        {
            return null;
        }

        return $"\"{path}\"";
    }
}
