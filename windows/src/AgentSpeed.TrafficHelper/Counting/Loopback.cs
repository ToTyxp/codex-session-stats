namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>Loopback addresses as Kernel-Network logs them (network byte order): 127.0.0.0/8, ::1, ::ffff:127.0.0.0/104.</summary>
internal static class Loopback
{
    public static bool IsIPv4(ReadOnlySpan<byte> address) => address.Length == 4 && address[0] == 127;

    public static bool IsIPv6(ReadOnlySpan<byte> address)
    {
        if (address.Length != 16)
        {
            return false;
        }

        if (address[..10].IndexOfAnyExcept((byte)0) >= 0)
        {
            return false;
        }

        if (address[10] == 0 && address[11] == 0)
        {
            return address[12..15].IndexOfAnyExcept((byte)0) < 0 && address[15] == 1;
        }

        return address[10] == 0xFF && address[11] == 0xFF && address[12] == 127;
    }
}
