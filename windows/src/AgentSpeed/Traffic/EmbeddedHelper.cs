using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AgentSpeed.Traffic;

/// <summary>The traffic helper this build carries for the running architecture (WINDOWS-SERVICE 2).</summary>
/// <param name="Sha256">Lowercase hex, already verified against <paramref name="Bytes"/>.</param>
internal sealed record EmbeddedHelperImage(byte[] Bytes, string Sha256);

/// <summary>
/// Reads the NativeAOT helper and its build-time SHA-256 from the manifest resources (<c>AgentSpeed.csproj</c>,
/// <c>TrafficHelperDir</c>). Whether a valid helper is present, and its hash, are checked once and cached; the bytes
/// (a few MB) are read again on each request so they do not stay in the app's memory.
/// </summary>
internal static class EmbeddedHelper
{
    public const string FileName = "AgentSpeedTrafficHelper.exe";

    private const int MaxSize = 64 * 1024 * 1024;

    private static readonly Lazy<string?> VerifiedHash = new(LoadVerifiedHash);

    /// <summary><c>win-x64</c> or <c>win-arm64</c>; null on other architectures (no helper is built for them).</summary>
    public static string? RuntimeId => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => null,
    };

    public static bool IsAvailable => VerifiedHash.Value is not null;

    /// <summary>The embedded helper's SHA-256 (lowercase hex), or null when this build carries none.</summary>
    public static string? Sha256 => VerifiedHash.Value;

    /// <summary>The helper for this architecture with its verified hash, or null when absent or damaged; never throws.</summary>
    public static EmbeddedHelperImage? ForCurrentArchitecture()
    {
        if (VerifiedHash.Value is not { } hash || RuntimeId is not { } rid)
        {
            return null;
        }

        var bytes = ReadResource($"AgentSpeed.TrafficHelper.{rid}.exe");
        return bytes is not null && HashOf(bytes) == hash ? new EmbeddedHelperImage(bytes, hash) : null;
    }

    public static string HashOf(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? LoadVerifiedHash()
    {
        if (RuntimeId is not { } rid)
        {
            return null;
        }

        var hashBytes = ReadResource($"AgentSpeed.TrafficHelper.{rid}.sha256");
        if (hashBytes is null || hashBytes.Length > 256 || ParseHash(System.Text.Encoding.ASCII.GetString(hashBytes)) is not { } expected)
        {
            return null;
        }

        var image = ReadResource($"AgentSpeed.TrafficHelper.{rid}.exe");
        return image is not null && HashOf(image) == expected ? expected : null;
    }

    /// <summary>64 hex digits (surrounding whitespace allowed), lowercased; null otherwise.</summary>
    internal static string? ParseHash(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length != 64)
        {
            return null;
        }

        foreach (var ch in trimmed)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return null;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static byte[]? ReadResource(string name)
    {
        try
        {
            using var stream = typeof(EmbeddedHelper).Assembly.GetManifestResourceStream(name);
            if (stream is null || stream.Length is <= 0 or > MaxSize)
            {
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }
}
