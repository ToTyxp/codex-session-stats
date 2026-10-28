using System.Globalization;
using System.Net.Http;
using AgentSpeed.Presentation.Updates;

namespace AgentSpeed.Updates;

/// <summary>Describes update errors for the log without paths, user names or other personal data.</summary>
internal static class UpdateLog
{
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = exception switch
        {
            UpdateException update => $"{update.Failure}: {update.Message}",
            HttpRequestException http => string.Create(CultureInfo.InvariantCulture,
                $"{nameof(HttpRequestException)} ({http.HttpRequestError}{(http.StatusCode is { } code ? $", HTTP {(int)code}" : string.Empty)})"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name} (0x{exception.HResult:X8})"),
        };

        return exception.InnerException is { } inner && exception is UpdateException or HttpRequestException
            ? $"{text} <- {Describe(inner)}"
            : text;
    }
}
