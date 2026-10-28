using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>
/// Reads an event's top-level property list with <c>TdhGetEventInformation</c> (TRACE_EVENT_INFO). Called once per
/// (provider, event ID, version); the buffer is bounded and every offset is checked against it.
/// </summary>
internal static unsafe class TdhLayoutReader
{
    private const int MaxInfoSize = 64 * 1024;
    private const int TopLevelPropertyCountOffset = 104;
    private const int PropertyArrayOffset = 112;
    private const int PropertyInfoSize = 24;

    public static List<EventProperty>? Read(EventRecord* record)
    {
        uint size = 0;
        var status = SystemLibraries.TdhGetEventInformation(record, 0, null, null, &size);
        if (status != Win32Error.InsufficientBuffer || size < PropertyArrayOffset || size > MaxInfoSize)
        {
            return null;
        }

        var buffer = (byte*)NativeMemory.AllocZeroed(size);
        try
        {
            if (SystemLibraries.TdhGetEventInformation(record, 0, null, buffer, &size) != 0)
            {
                return null;
            }

            return Parse(new ReadOnlySpan<byte>(buffer, (int)size));
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>Parses TRACE_EVENT_INFO's EVENT_PROPERTY_INFO array (names are NUL-terminated UTF-16 at their offsets).</summary>
    public static List<EventProperty>? Parse(ReadOnlySpan<byte> info)
    {
        if (info.Length < PropertyArrayOffset)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(info[TopLevelPropertyCountOffset..]);
        if (count > 256 || PropertyArrayOffset + count * PropertyInfoSize > info.Length)
        {
            return null;
        }

        var properties = new List<EventProperty>((int)count);
        for (var i = 0; i < (int)count; i++)
        {
            var entry = info.Slice(PropertyArrayOffset + i * PropertyInfoSize, PropertyInfoSize);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var inType = BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]);
            var propertyCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[16..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(entry[18..]);
            properties.Add(new EventProperty(ReadName(info, nameOffset), inType, flags, propertyCount, length));
        }

        return properties;
    }

    private static string ReadName(ReadOnlySpan<byte> info, uint offset)
    {
        if (offset == 0 || offset >= info.Length - 1)
        {
            return "";
        }

        var chars = MemoryMarshal.Cast<byte, char>(info[(int)offset..][..((info.Length - (int)offset) & ~1)]);
        var end = chars.IndexOf('\0');
        return end < 0 || end > 128 ? "" : chars[..end].ToString();
    }
}
