using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>tdh, ntdll and bcrypt entry points.</summary>
internal static unsafe partial class SystemLibraries
{
    public const int SystemProcessInformation = 5;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>BCRYPT_SHA256_ALG_HANDLE / BCRYPT_SHA1_ALG_HANDLE pseudo-handles (Windows 10+).</summary>
    public const nint Sha256Algorithm = 0x41;
    public const nint Sha1Algorithm = 0x31;

    [LibraryImport("tdh.dll")]
    public static partial uint TdhGetEventInformation(void* eventRecord, uint contextCount, void* context, void* buffer, uint* bufferSize);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int informationClass, void* buffer, uint length, uint* returnLength);

    [LibraryImport("bcrypt.dll")]
    public static partial int BCryptHash(nint algorithm, byte* secret, uint secretLength, byte* input, uint inputLength, byte* output, uint outputLength);

    /// <summary>SHA-256 through CNG (no managed crypto stack in the NativeAOT image).</summary>
    public static bool Sha256(ReadOnlySpan<byte> data, Span<byte> hash) => Hash(Sha256Algorithm, data, hash, 32);

    public static bool Sha1(ReadOnlySpan<byte> data, Span<byte> hash) => Hash(Sha1Algorithm, data, hash, 20);

    private static bool Hash(nint algorithm, ReadOnlySpan<byte> data, Span<byte> hash, int length)
    {
        if (hash.Length < length)
        {
            return false;
        }

        fixed (byte* input = data)
        fixed (byte* output = hash)
        {
            return BCryptHash(algorithm, null, 0, input, (uint)data.Length, output, (uint)length) == 0;
        }
    }
}
