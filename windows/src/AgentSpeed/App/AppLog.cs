using System.Globalization;
using System.Text;

namespace AgentSpeed.App;

/// <summary>
/// Minimal file log for diagnostics. Records only app events, Win32 error codes and exception details;
/// never conversation content.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _disabled;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder(256);
            builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append('(').Append(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)).Append(") ")
                .Append(message);
            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            builder.AppendLine();

            lock (Gate)
            {
                var path = EnsurePath();
                Rotate(path);
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _disabled = true;
        }
    }

    private static string EnsurePath()
    {
        if (_path is not null)
        {
            return _path;
        }

        var directory = AppPaths.LogsDirectory;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "agentspeed.log");
        return _path;
    }

    private static void Rotate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var previous = path + ".1";
        File.Move(path, previous, overwrite: true);
    }
}
