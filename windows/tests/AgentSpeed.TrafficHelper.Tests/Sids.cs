using System.Buffers.Binary;

namespace AgentSpeed.TrafficHelper.Tests;

/// <summary>Builds binary SIDs and ACLs the way Windows lays them out.</summary>
internal static class Sids
{
    public static byte[] Binary(string sid)
    {
        var parts = sid.Split('-');
        var subAuthorities = parts.Skip(3).Select(uint.Parse).ToArray();
        var bytes = new byte[8 + subAuthorities.Length * 4];
        bytes[0] = 1;
        bytes[1] = (byte)subAuthorities.Length;
        var authority = ulong.Parse(parts[2]);
        for (var i = 0; i < 6; i++)
        {
            bytes[7 - i] = (byte)(authority >> (8 * i));
        }

        for (var i = 0; i < subAuthorities.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + i * 4), subAuthorities[i]);
        }

        return bytes;
    }

    public static byte[] Ace(byte type, byte flags, uint mask, string sid)
    {
        var sidBytes = Binary(sid);
        var ace = new byte[8 + sidBytes.Length];
        ace[0] = type;
        ace[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(ace.AsSpan(2), (ushort)ace.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(4), mask);
        sidBytes.CopyTo(ace, 8);
        return ace;
    }

    public static byte[] Acl(params byte[][] aces)
    {
        var size = 8 + aces.Sum(a => a.Length);
        var acl = new byte[size];
        acl[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(2), (ushort)size);
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4), (ushort)aces.Length);
        var offset = 8;
        foreach (var ace in aces)
        {
            ace.CopyTo(acl, offset);
            offset += ace.Length;
        }

        return acl;
    }
}
